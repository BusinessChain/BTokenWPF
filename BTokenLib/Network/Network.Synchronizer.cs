using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Dynamic;


namespace BTokenLib
{
  partial class Network
  {
    const int TIME_LOOP_SYNCHRONIZER_SECONDS = 60;

    readonly object LOCK_IsStateSync = new();
    public bool IsStateSync;
    public DateTime TimeStartLastSync;

    bool FlagSyncAbort;
    int HeightInsertion;
    object LOCK_BlockInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, Header> HeadersDownloading = new();
    const int TIMEOUT_BLOCKDOWNLOAD_SECONDS = 120; // If we don't get a specific block from any peers for this time, abort the synchronization process with peerSync.
    int CountdownTimeoutBlockDownloadSeconds;
    Header HeaderDownloadNext;

    bool FlagSyncDBAbort;
    List<byte[]> HashesDB;

    object LOCK_FetchHeaderDownload = new();
    ConcurrentBag<Block> PoolBlocks = new();

    object LOCK_ChargeHashDB = new();


    public void StartSync()
    {
      $"Try start synchronization of token {Token.GetName()}. Send getheaders to all peers."
        .Log(this, Token.LogFile, Token.LogEntryNotifier);

      if (IsStateSync)
        return;

      foreach(Peer peer in Peers)
        try
        {
          peer.SendGetHeaders(Token.GetLocator());
        }
        catch
        {
          $"Could not start synchronization with peer {peer}.".Log(this, Token.LogEntryNotifier);
        }
    }

    async Task SyncBlocks(HeaderchainDownload headerchainDownload)
    {
      lock (LOCK_IsStateSync)
      {
        if (IsStateSync)
          return;

        if (!Token.TryLock())
          return;

        IsStateSync = true;
      }

      HeadersDownloading.Clear();
      QueueBlockInsertion.Clear();
      CountdownTimeoutBlockDownloadSeconds = 0;
      FlagSyncAbort = false;

      double difficultyAccumulatedOld = Token.HeaderTip.DifficultyAccumulated;

      if (headerchainDownload.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
        try
        {
          HeaderDownloadNext = headerchainDownload.HeaderRoot;
          HeightInsertion = HeaderDownloadNext.Height;

          if (HeaderDownloadNext.HeaderPrevious != Token.HeaderTip)
          {
            $"Forking chain after common ancestor {HeaderDownloadNext.HeaderPrevious} with height {HeaderDownloadNext.HeaderPrevious.Height}."
              .Log(this, Token.LogFile, Token.LogEntryNotifier);

            if (Token.TryReverseBlockchainToHeight(HeaderDownloadNext.HeaderPrevious.Height))
              Token.Archiver.SetBlockPathToFork();
            else
              Token.Reset(); //Restart Sync as if cold start.
          }

          int HeightHeaderTipOld = Token.HeaderTip.Height;

          while (true)
          {
            if (Token.HeaderTip.Height == headerchainDownload.HeaderTip.Height)
              break;

            if (CountdownTimeoutBlockDownloadSeconds > TIMEOUT_BLOCKDOWNLOAD_SECONDS)
              FlagSyncAbort = true;
            else
            {
              if (HeightHeaderTipOld == Token.HeaderTip.Height)
                CountdownTimeoutBlockDownloadSeconds += 1;
              else
              {
                HeightHeaderTipOld = Token.HeaderTip.Height;
                CountdownTimeoutBlockDownloadSeconds = 0;
              }
            }

            if (FlagSyncAbort) // überprüfe den ganzen abort Zyklus, aber vielleicht zuerst mal grafisch darstellen
            {
              $"Synchronization is aborted.".Log(this, Token.LogFile, Token.LogEntryNotifier);

              Token.LoadState();

              foreach (Peer p in Peers.Where(p => p.IsStateSync()))
                p.SetStateIdle();

              break;
            }

            if (TryFetchHeaderDownload(out Header headerDownload))
              if (TryGetPeerIdle(out Peer peer, Peer.StateProtocol.Sync))
              {
                if (!PoolBlocks.TryTake(out Block blockDownload))
                  blockDownload = new Block(Token);

                peer.RequestBlock(headerDownload, blockDownload);
              }

            await Task.Delay(1000).ConfigureAwait(false);
          }
        }
        catch (Exception ex)
        {
          ($"Unexpected exception {ex.GetType().Name} occured during SyncBlocks.\n" +
            $"{ex.Message}").Log(this, Token.LogFile, Token.LogEntryNotifier);
        }

      if (Token.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
        Token.Archiver.Reorganize();
      else
        Token.LoadState();

      ExitStateSync();

      $"Synchronization of {Token.GetName()} completed.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      foreach (Token tokenChild in Token.TokensChild)
        tokenChild.Network.StartSync();
    }

    void ExitStateSync()
    {
      $"Exiting state synchronization of token {Token.GetName()}.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      FlagSyncAbort = true;
      IsStateSync = false;
      Token.ReleaseLock();
    }

    void InsertBlock(Block block, int heightBlock)
    {
      lock (LOCK_FetchHeaderDownload)
        HeadersDownloading.Remove(heightBlock);

      lock (LOCK_BlockInsertion) // evt. mit lock this arbeiten
      {
        if (heightBlock < HeightInsertion || QueueBlockInsertion.ContainsKey(heightBlock))
          PoolBlocks.Add(block);
        if (heightBlock > HeightInsertion)
          QueueBlockInsertion.Add(heightBlock, block);
        else
          do
          {
            try
            {
              Token.InsertBlock(block);
            }
            catch (Exception ex)
            {
              $"Abort Sync. Insertion of block {block} failed:\n {ex.Message}.".Log(this, Token.LogFile, Token.LogEntryNotifier);
              FlagSyncAbort = true;
              return;
            }

            PoolBlocks.Add(block);

            HeightInsertion += 1;

          } while (QueueBlockInsertion.Remove(HeightInsertion, out block));
      }
    }

    bool TryFetchHeaderDownload(out Header headerDownload)
    {
      headerDownload = null;

      lock (LOCK_FetchHeaderDownload)
        if (HeadersDownloading.Any() && (QueueBlockInsertion.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null))
          headerDownload = HeadersDownloading[HeadersDownloading.Keys.Min()];
        else if (HeaderDownloadNext != null)
        {
          headerDownload = HeaderDownloadNext;
          HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
          HeadersDownloading.Add(headerDownload.Height, headerDownload);
        }

      return headerDownload != null;
    }

    async Task SyncDB(Peer peer)
    {
    }

  }
}
