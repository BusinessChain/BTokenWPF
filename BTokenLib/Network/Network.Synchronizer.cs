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
    List<byte[]> QueueHashesDBDownloadIncomplete = new();


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
      Peers.ForEach(p => p.HeaderDownload = null);

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

          Header headerSync;
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

            if (FlagSyncAbort)
            {
              $"Synchronization is aborted.".Log(this, Token.LogFile, Token.LogEntryNotifier);

              Token.LoadState();

              foreach (Peer p in Peers.Where(p => p.IsStateSync()))
                p.SetStateIdle();

              break;
            }

            if (TryFetchHeaderDownload(out headerSync))
              if (TryGetPeerIdle(out Peer peer))
                peer.RequestBlock();

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

    void InsertBlock(Peer peer)
    {
      int heightBlock = peer.HeaderDownload.Height;
      peer.HeaderDownload = null;

      Block block = peer.BlockDownload;
      peer.BlockDownload = null;

      lock (LOCK_FetchHeaderDownload)
        HeadersDownloading.Remove(heightBlock);

      lock (LOCK_BlockInsertion)
      {
        if (heightBlock > HeightInsertion && !QueueBlockInsertion.ContainsKey(heightBlock))
          QueueBlockInsertion.Add(heightBlock, block);
        else if (heightBlock == HeightInsertion)
          while (true)
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

            HeightInsertion += 1;

            PoolBlocks.Add(block);

            if (!QueueBlockInsertion.TryGetValue(HeightInsertion, out block))
              break;

            QueueBlockInsertion.Remove(HeightInsertion);
          }
      }
    }

    bool TryFetchHeaderDownload(out Header headerDownload)
    {

      if (!PoolBlocks.TryTake(out peer.BlockDownload))
        peer.BlockDownload = new Block(Token); //ein neuer BlockDownload wird im FetchHeader gegeben.

      // gib statt headerDownload ein BlockDownload zurück, der als header den headerDownload hat.


      headerDownload = null;

      lock (LOCK_FetchHeaderDownload)
      {
        if (QueueBlockInsertion.Count > CAPACITY_MAX_QueueBlocksInsertion || (HeaderDownloadNext == null && HeadersDownloading.Any()))
          headerDownload = HeadersDownloading[HeadersDownloading.Keys.Min()];
        else if (HeaderDownloadNext != null)
        {
          headerDownload = HeaderDownloadNext;
          HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
          HeadersDownloading.Add(headerDownload.Height, headerDownload);
        }

        return false;
      }
    }

    async Task SyncDB(Peer peer)
    {
    }

    bool InsertDB_FlagContinue(Peer peer)
    {
      try
      {
        Token.InsertDB(peer.Payload, peer.LengthDataPayload);

        $"Inserted DB {peer.HashDBDownload.ToHexString()}."
        .Log(this, Token.LogFile, Token.LogEntryNotifier);
      }
      catch (Exception ex)
      {
        ($"Insertion of DB {peer.HashDBDownload.ToHexString()} failed:\n " +
          $"{ex.Message}.").Log(this, Token.LogFile, Token.LogEntryNotifier);

        FlagSyncAbort = true;

        return false;
      }

      return TryChargeHashDB(peer);
    }

    bool TryChargeHashDB(Peer peer)
    {
      lock (LOCK_ChargeHashDB)
      {
        if (QueueHashesDBDownloadIncomplete.Any())
        {
          peer.HashDBDownload = QueueHashesDBDownloadIncomplete[0];
          QueueHashesDBDownloadIncomplete.RemoveAt(0);

          return true;
        }

        if (HashesDB.Any())
        {
          peer.HashDBDownload = HashesDB[0];
          HashesDB.RemoveAt(0);

          return true;
        }

        return false;
      }
    }

  }
}
