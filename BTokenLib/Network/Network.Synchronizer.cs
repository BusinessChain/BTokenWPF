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
    Peer PeerSync;

    bool FlagSyncAbort;
    int HeightInsertion;
    object LOCK_HeightInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, (Header, int)> HeadersDownloadingByCountPeers = new();
    Dictionary<int, Header> QueueDownloadsIncomplete = new();
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

    void HandleExceptionPeerListener(Peer peer)
    {
      $"HandleExceptionPeerListener {peer}".Log(this, Token.LogFile, Token.LogEntryNotifier);

      lock (LOCK_IsStateSync) lock (LOCK_Peers)
        {          
          if (peer.IsStateBlockSync())
          {
            $"ReturnPeerBlockDownloadIncomplete".Log(this, Token.LogFile, Token.LogEntryNotifier);
            ReturnPeerBlockDownloadIncomplete(peer);
          }
          else if (IsStateSync && PeerSync == peer)
            ExitStateSync();
          else if (peer.IsStateDBDownload())
            ReturnPeerDBDownloadIncomplete(peer.HashDBDownload);
        }
    }

    async Task SyncBlocks(Peer peer)
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
      FlagSyncAbort = false;
      PeerSync = peer;
      Peers.ForEach(p => p.HeaderSync = null);

      $"Enter state synchronization of token {Token.GetName()} with peer {PeerSync}."
        .Log(this, Token.LogFile, Token.LogEntryNotifier);

      double difficultyAccumulatedOld = Token.HeaderTip.DifficultyAccumulated;

      if (PeerSync.HeaderDownload.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
        try
        {
          HeaderRoot = PeerSync.HeaderDownload.HeaderRoot;
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

          while (true)
          {
            if (FlagSyncAbort)
            {
              $"Synchronization with {PeerSync} is aborted.".Log(this, Token.LogFile, Token.LogEntryNotifier);

              Token.LoadState();

              foreach (Peer p in Peers.Where(p => p.IsStateBlockSync())) //mit IsStateSync
                p.SetStateIdle();

              break;
            }

            if (peer != null)
            {
              if (TryFetchHeaderSync(peer))
                peer.RequestBlock();
              else
              {
                peer.SetStateIdle();

                if (Peers.All(p => !p.IsStateBlockSync()))
                {
                  if (Token.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
                    Token.Archiver.Reorganize();
                  else
                    Token.LoadState();

                  break;
                }
              }
            }

            TryGetPeerIdle(out peer);

            await Task.Delay(1000).ConfigureAwait(false);
          }
        }
        catch (Exception ex)
        {
          ($"Unexpected exception {ex.GetType().Name} occured during SyncBlocks.\n" +
            $"{ex.Message}").Log(this, Token.LogFile, Token.LogEntryNotifier);
        }
      else if (peer.HeaderDownload.HeaderTip.DifficultyAccumulated < difficultyAccumulatedOld)
        PeerSync.SendHeaders(new List<Header>() { Token.HeaderTip });

      ExitStateSync();

      $"Synchronization of {Token.GetName()} completed.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      foreach (Token tokenChild in Token.TokensChild)
        tokenChild.Network.StartSync();
    }

    void ExitStateSync()
    {
      $"Exiting state synchronization of token {Token.GetName()} with peer {PeerSync}."
        .Log(this, Token.LogFile, Token.LogEntryNotifier);

      FlagSyncAbort = true;
      IsStateSync = false;
      PeerSync.SetStateIdle();
      PeerSync.TimeLastSync = DateTime.Now;
      PeerSync = null;
      Token.ReleaseLock();
    }

    void InsertBlock(Peer peer)
    {
      if (HeadersDownloadingByCountPeers.ContainsKey(peer.HeaderSync.Height))
      {
        (Header headerBeingDownloaded, int countPeers) =
          HeadersDownloadingByCountPeers[peer.HeaderSync.Height];

        if (countPeers > 1)
          HeadersDownloadingByCountPeers[peer.HeaderSync.Height] =
            (headerBeingDownloaded, countPeers - 1);
        else
          HeadersDownloadingByCountPeers.Remove(peer.HeaderSync.Height);
      }

      Block block = peer.BlockSync;

      lock (LOCK_HeightInsertion)
      {
        if (peer.HeaderSync.Height > HeightInsertion)
        {
          QueueBlockInsertion.Add(peer.HeaderSync.Height, block);

          if (!PoolBlocks.TryTake(out peer.BlockSync))
            peer.BlockSync = new Block(Token);
        }
        else if (peer.HeaderSync.Height == HeightInsertion)
        {
          bool isBlockFromQueue = false;

          while (true)
          {
            try
            {
              $"Insert block {Token.HeaderTip.Height}, {block}.".Log(this, Token.LogFile, Token.LogEntryNotifier);
              Token.InsertBlock(block);
              block.Clear();
            }
            catch (Exception ex)
            {
              $"Abort Sync. Insertion of block {block} failed:\n {ex.Message}.".Log(this, Token.LogFile, Token.LogEntryNotifier);
              FlagSyncAbort = true;
              return; // do something bad to peer
            }

            HeightInsertion += 1;

            if (isBlockFromQueue)
              PoolBlocks.Add(block);

            if (!QueueBlockInsertion.TryGetValue(HeightInsertion, out block))
              break;

            QueueBlockInsertion.Remove(HeightInsertion);
            isBlockFromQueue = true;
          }
        }
      }
    }

    bool TryFetchHeaderSync(Peer peer)
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

          peer.HeaderSync = headerBeingDownloadedMinHeight;
          return true;
        }

      LABEL_FetchingWithHeaderMinHeight:

        while (QueueDownloadsIncomplete.Any())
        {
          int heightSmallestHeadersIncomplete = QueueDownloadsIncomplete.Keys.Min();
          Header header = QueueDownloadsIncomplete[heightSmallestHeadersIncomplete];
          QueueDownloadsIncomplete.Remove(heightSmallestHeadersIncomplete);

          lock (LOCK_HeightInsertion)
            if (heightSmallestHeadersIncomplete < HeightInsertion)
              continue;

          peer.HeaderSync = header;

          return true;
        }

        if (HeaderRoot != null)
        {
          peer.HeaderSync = HeaderRoot;
          HeaderRoot = HeaderRoot.HeaderNext;

          HeadersDownloadingByCountPeers.Add(peer.HeaderSync.Height, (peer.HeaderSync, 1));

          return true;
        }

        return false;
      }
    }

    void ReturnPeerBlockDownloadIncomplete(Peer peer)
    {
      lock (LOCK_FetchHeaderSync)
        if (QueueDownloadsIncomplete.ContainsKey(peer.HeaderSync.Height))
        {
          (Header headerBeingDownloaded, int countPeers) =
            HeadersDownloadingByCountPeers[peer.HeaderSync.Height];

          if (countPeers > 1)
            HeadersDownloadingByCountPeers[peer.HeaderSync.Height] =
              (headerBeingDownloaded, countPeers - 1);
        }
        else
        {
          QueueDownloadsIncomplete.Add(
            peer.HeaderSync.Height,
            peer.HeaderSync);

          $"Add download {peer.HeaderSync} to QueueDownloadsIncomplete.".Log(this, Token.LogFile, Token.LogEntryNotifier);
        }
    }

    async Task SyncDB(Peer peer)
    {
      Peer peerSync = peer;
      HashesDB = peerSync.HashesDB;

      Token.DeleteDB();

      while (true)
      {
        if (FlagSyncDBAbort)
        {
          $"Synchronization with {peerSync} was abort."
            .Log(this, Token.LogFile, Token.LogEntryNotifier);

          Token.LoadState();

          lock (LOCK_Peers)
            Peers
              .Where(p => p.IsStateDBDownload()).ToList()
              .ForEach(p => p.SetStateIdle());

          while (true)
          {
            lock (LOCK_Peers)
              if (!Peers.Any(p => p.IsStateDBDownload()))
                break;

            "Waiting for all peers to exit state 'synchronization busy'."
              .Log(this, Token.LogFile, Token.LogEntryNotifier);

            await Task.Delay(1000).ConfigureAwait(false);
          }

          break;
        }

        if (peer != null)
          if (TryChargeHashDB(peer))
            await peer.RequestDB();
          else
          {
            peer.SetStateIdle();

            if (Peers.All(p => !p.IsStateBlockSync()))
              break;
          }

        TryGetPeerIdle(out peer);

        await Task.Delay(1000).ConfigureAwait(false);
      }
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
