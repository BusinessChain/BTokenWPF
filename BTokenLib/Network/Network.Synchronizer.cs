using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;


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
    object LOCK_HeightInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, (Header, int)> HeadersDownloadingByCountPeers = new();
    Dictionary<int, Header> QueueDownloadsIncomplete = new();
    const int TIMEOUT_BLOCKDOWNLOAD_SECONDS = 120; // If we don't get a specific block from any peers for this time, abort the synchronization process with peerSync.
    int CountdownTimeoutBlockDownloadSeconds;
    Header HeaderRoot;

    bool FlagSyncDBAbort;
    List<byte[]> HashesDB;

    object LOCK_FetchHeaderSync = new();
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

    async Task SyncBlocks(HeaderDownload headerDownload)
    {
      lock (LOCK_IsStateSync)
      {
        if (IsStateSync)
          return;

        if (!Token.TryLock())
          return;

        IsStateSync = true;
      }

      HeadersDownloadingByCountPeers.Clear();
      QueueDownloadsIncomplete.Clear();
      QueueBlockInsertion.Clear();
      CountdownTimeoutBlockDownloadSeconds = 0;
      FlagSyncAbort = false;
      Peers.ForEach(p => p.HeaderSync = null);

      double difficultyAccumulatedOld = Token.HeaderTip.DifficultyAccumulated;

      if (headerDownload.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
        try
        {
          HeaderRoot = headerDownload.HeaderRoot;
          HeightInsertion = HeaderRoot.Height;

          if (HeaderRoot.HeaderPrevious != Token.HeaderTip)
          {
            $"Forking chain after common ancestor {HeaderRoot.HeaderPrevious} with height {HeaderRoot.HeaderPrevious.Height}."
              .Log(this, Token.LogFile, Token.LogEntryNotifier);

            if (Token.TryReverseBlockchainToHeight(HeaderRoot.HeaderPrevious.Height))
              Token.Archiver.SetBlockPathToFork();
            else
              Token.Reset(); //Restart Sync as if cold start.
          }

          Header headerSync;
          int HeightHeaderTipOld = Token.HeaderTip.Height;

          while (true)
          {
            if (Token.HeaderTip.Height == headerDownload.HeaderTip.Height)
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

            if (TryFetchHeaderSync(out headerSync))
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

    void InsertBlock(Block blockSync, int heightBlock)
    {
      if (HeadersDownloadingByCountPeers.ContainsKey(heightBlock))
      {
        (Header headerBeingDownloaded, int countPeers) =
          HeadersDownloadingByCountPeers[heightBlock];

        if (countPeers > 1)
          HeadersDownloadingByCountPeers[heightBlock] =
            (headerBeingDownloaded, countPeers - 1);
        else
          HeadersDownloadingByCountPeers.Remove(heightBlock);
      }

      lock (LOCK_HeightInsertion)
      {
        if (heightBlock > HeightInsertion)
        {
          QueueBlockInsertion.Add(heightBlock, blockSync);

          if (!PoolBlocks.TryTake(out blockSync))
            blockSync = new Block(Token);
        }
        else if (heightBlock == HeightInsertion)
        {
          bool isBlockFromQueue = false;

          while (true)
          {
            try
            {
              $"Insert block {Token.HeaderTip.Height}, {blockSync}.".Log(this, Token.LogFile, Token.LogEntryNotifier);
              Token.InsertBlock(blockSync);
              blockSync.Clear();
            }
            catch (Exception ex)
            {
              $"Abort Sync. Insertion of block {blockSync} failed:\n {ex.Message}.".Log(this, Token.LogFile, Token.LogEntryNotifier);
              FlagSyncAbort = true;
              return; // do something bad to peer
            }

            HeightInsertion += 1;

            if (isBlockFromQueue)
              PoolBlocks.Add(blockSync);

            if (!QueueBlockInsertion.TryGetValue(HeightInsertion, out blockSync))
              break;

            QueueBlockInsertion.Remove(HeightInsertion);
            isBlockFromQueue = true;
          }
        }
      }
    }

    bool TryFetchHeaderSync(out Header headerSync)
    {
      lock (LOCK_FetchHeaderSync)
      {
        if (QueueBlockInsertion.Count > CAPACITY_MAX_QueueBlocksInsertion)
        {
          int keyHeightMin = HeadersDownloadingByCountPeers.Keys.Min();

          (Header headerBeingDownloadedMinHeight, int countPeersMinHeight) =
            HeadersDownloadingByCountPeers[keyHeightMin];

          lock (LOCK_HeightInsertion)
            if (keyHeightMin < HeightInsertion)
              goto LABEL_FetchingWithHeaderMinHeight;

          HeadersDownloadingByCountPeers[keyHeightMin] =
            (headerBeingDownloadedMinHeight, countPeersMinHeight + 1);

          headerSync = headerBeingDownloadedMinHeight;
          return true;
        }
        //else
        // keine neuen mehr, aber noch downloads in progress sind, wird in jedem Fall von hier genommen

      LABEL_FetchingWithHeaderMinHeight:

        while (QueueDownloadsIncomplete.Any())
        {
          int heightSmallestHeadersIncomplete = QueueDownloadsIncomplete.Keys.Min();
          Header header = QueueDownloadsIncomplete[heightSmallestHeadersIncomplete];
          QueueDownloadsIncomplete.Remove(heightSmallestHeadersIncomplete);

          lock (LOCK_HeightInsertion)
            if (heightSmallestHeadersIncomplete < HeightInsertion)
              continue;

          headerSync = header;
          return true;
        }

        if (HeaderRoot != null)
        {
          headerSync = HeaderRoot;
          HeaderRoot = HeaderRoot.HeaderNext;

          HeadersDownloadingByCountPeers.Add(headerSync.Height, (headerSync, 1));

          return true;
        }

        headerSync = null;
        return false;
      }
    }

    void ReturnBlockDownloadIncomplete(Header headerSync)
    {
      lock (LOCK_FetchHeaderSync)
        if (QueueDownloadsIncomplete.ContainsKey(headerSync.Height))
        {
          (Header headerBeingDownloaded, int countPeers) =
            HeadersDownloadingByCountPeers[headerSync.Height];

          if (countPeers > 1)
            HeadersDownloadingByCountPeers[headerSync.Height] =
              (headerBeingDownloaded, countPeers - 1);
        }
        else
        {
          QueueDownloadsIncomplete.Add(
            headerSync.Height,
            headerSync);

          $"Add download {headerSync} to QueueDownloadsIncomplete.".Log(this, Token.LogFile, Token.LogEntryNotifier);
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

    void ReturnPeerDBDownloadIncomplete(byte[] hashDBSync)
    {
      QueueHashesDBDownloadIncomplete.Add(hashDBSync);
    }
  }
}
