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
        peer.SendGetHeaders(Token.GetLocator());
    }

    Peer PeerSync;
    HeaderchainDownload HeaderchainDownload;
    bool FlagSyncHeadersComplete;
    bool FlagSyncHeadersFailed;
    const int TIMEOUT_HEADERSYNC_SECONDS = 5;
    double TimeoutAdjustementFactor = 1.0;
    Dictionary<Peer, List<byte[]>> PeersWhereHeadersSyncFailed = new();

    bool TryReceiveHeadersMessage(Peer peer)
    {
      lock (LOCK_IsStateSync)
        if (peer != PeerSync)
        {
          if (IsStateSync || !Token.TryLock())
            return false;

          IsStateSync = true;
          PeerSync = peer;
          HeaderchainDownload = new HeaderchainDownload(Token.GetLocator());
          FlagSyncHeadersComplete = false;
          FlagSyncHeadersFailed = false;

          StartTimerSyncHeaders();
        }
        else if (FlagSyncHeadersComplete || FlagSyncHeadersFailed)
          return false;

      int startIndex = 0;
      int countHeaders = VarInt.GetInt(peer.Payload, ref startIndex);

      if (countHeaders > 0)
      {
        int i = 0;
        while (i < countHeaders)
        {
          Header header = Token.ParseHeader(peer.Payload, ref startIndex, peer.SHA256);
          startIndex += 1; // Number of transaction entries, this value is always 0

          if (!HeaderchainDownload.TryInsertHeader(header, out bool flagIsHeaderRoot))
            break;
          else if(flagIsHeaderRoot && PeersWhereHeadersSyncFailed[PeerSync].Any(hash => hash.IsAllBytesEqual(header.Hash)))
            break;

          i += 1;
        }

        PeerSync.SendGetHeaders(HeaderchainDownload.Locator);

        if (i == countHeaders)
          return true;
      }
      else if (HeaderchainDownload.HeaderTip?.DifficultyAccumulated > Token.HeaderTip.DifficultyAccumulated)
      {
        FlagSyncHeadersComplete = true;
        SyncBlocks();
      }

      return false;
    }

    async Task StartTimerSyncHeaders()
    {
      Header HeaderTipOld = HeaderchainDownload.HeaderTip;
      int countdownTimeoutSeconds = 0;

      while (!FlagSyncHeadersComplete)
      {
        if (countdownTimeoutSeconds < TIMEOUT_HEADERSYNC_SECONDS * TimeoutAdjustementFactor)
        {
          if (HeaderTipOld == HeaderchainDownload.HeaderTip)
            countdownTimeoutSeconds += 1;
          else
          {
            HeaderTipOld = HeaderchainDownload.HeaderTip;
            countdownTimeoutSeconds = 0;
          }
        }
        else
        {
          $"Exiting header synchronization of token {Token.GetName()} due to timeout in synchronizator.".Log(this, Token.LogFile, Token.LogEntryNotifier);
          
          lock (LOCK_IsStateSync)
          {
            FlagSyncHeadersFailed = true;
            IsStateSync = false;
            Token.ReleaseLock();
          }

          break;
        }

        await Task.Delay(1000).ConfigureAwait(false);
      }
    }


    int HeightInsertion;
    object LOCK_BlockInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, Header> HeadersDownloading = new();
    const int TIMEOUT_BLOCKDOWNLOAD_SECONDS = 120; // If we don't get a specific block from any peers for this time, abort the synchronization process with peerSync.
    Header HeaderDownloadNext;

    bool FlagSyncBlocksComplete;
    bool FlagSyncAbort;

    async Task SyncBlocks()
    {
      $"Start block synchronization.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      HeadersDownloading.Clear();
      QueueBlockInsertion.Clear();
      FlagSyncAbort = false;
      FlagSyncBlocksComplete = false;

      double difficultyAccumulatedOld = Token.HeaderTip.DifficultyAccumulated;

      HeaderDownloadNext = HeaderchainDownload.HeaderRoot;
      HeightInsertion = HeaderDownloadNext.Height;

      if (HeaderDownloadNext.HeaderPrevious != Token.HeaderTip)
      {
        $"Forking chain after common ancestor {HeaderDownloadNext.HeaderPrevious} with height {HeaderDownloadNext.HeaderPrevious.Height}."
          .Log(this, Token.LogFile, Token.LogEntryNotifier);

        if (Token.TryReverseBlockchainToHeight(HeaderDownloadNext.HeaderPrevious.Height))
          Token.Archiver.SetBlockPathToFork();
        else
        {
          Token.Reset();
          Token.LoadState();

          lock (LOCK_IsStateSync)
          {
            Token.ReleaseLock();
            IsStateSync = false;
            FlagSyncBlocksComplete = true;
            PeerSync = null;
          }

          PeerSync.SendGetHeaders(Token.GetLocator());
          return;
        }
      }

      StartTimerSyncBlocks();

      while (Token.HeaderTip.Height < HeaderchainDownload.HeaderTip.Height && !FlagSyncAbort)
      {
        if (TryGetPeerIdle(out Peer peer))
          if (TryFetchHeaderDownload(out Header headerDownload))
          {
            if (!PoolBlocks.TryTake(out Block blockDownload))
              blockDownload = new Block(Token);

            peer.RequestBlock(headerDownload, blockDownload);
          }
          else
            peer.SetStateIdle();

        await Task.Delay(100).ConfigureAwait(false);
      }

      if (Token.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
        Token.Archiver.Reorganize(); //Lösche blöcke der alten chain, mache neu fork chain zur main chain
      else
      {
        Token.LoadState(); // Reverse wieder die Fork chain, und synchronisiere wieder auf die Mainchain.

        if (PeersWhereHeadersSyncFailed.TryGetValue(PeerSync, out List<byte[]> headersWhereSyncFailed))
          headersWhereSyncFailed.Add(HeaderchainDownload.HeaderRoot.Hash);
        else
          PeersWhereHeadersSyncFailed.Add(PeerSync, new List<byte[]> { HeaderchainDownload.HeaderRoot.Hash });
      }

      lock (LOCK_IsStateSync)
      {
        Token.ReleaseLock();
        IsStateSync = false;
        FlagSyncBlocksComplete = true;
        PeerSync = null;
      }

      $"Synchronization of {Token.GetName()} completed.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      foreach (Token tokenChild in Token.TokensChild)
        tokenChild.Network.StartSync();
    }

    async Task StartTimerSyncBlocks()
    {
      int heightHeaderTipOld = Token.HeaderTip.Height;
      int countdownTimeoutSeconds = 0;

      while (!FlagSyncBlocksComplete)
      {
        if (countdownTimeoutSeconds < TIMEOUT_BLOCKDOWNLOAD_SECONDS * TimeoutAdjustementFactor)
        {
          if (heightHeaderTipOld == Token.HeaderTip.Height)
            countdownTimeoutSeconds += 1;
          else
          {
            heightHeaderTipOld = Token.HeaderTip.Height;
            countdownTimeoutSeconds = 0;
          }
        }
        else
        {
          $"Exiting block synchronization of token {Token.GetName()} due to timeout in synchronizator.".Log(this, Token.LogFile, Token.LogEntryNotifier);
          FlagSyncAbort = true;
          break;
        }

        await Task.Delay(1000).ConfigureAwait(false);
      }
    }

    void InsertBlock(Peer peer)
    {
      Header headerDownload = peer.HeaderDownload;
      Block block = peer.BlockDownload;

      peer.HeaderDownload = null;
      peer.BlockDownload = null;
      peer.SetStateIdle();

      lock (LOCK_FetchHeaderDownload)
      {
        var itemHeaderDownloading = HeadersDownloading
          .FirstOrDefault(h => h.Value.Hash.IsAllBytesEqual(headerDownload.Hash));

        if (itemHeaderDownloading.Value == null)
          return;

        HeadersDownloading.Remove(itemHeaderDownloading.Key);
      }

      int heightBlock = headerDownload.Height;

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
        if ((QueueBlockInsertion.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null) && HeadersDownloading.Any())
          headerDownload = HeadersDownloading[HeadersDownloading.Keys.Min()];
        else if (HeaderDownloadNext != null)
        {
          headerDownload = HeaderDownloadNext;
          HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
          HeadersDownloading.Add(headerDownload.Height, headerDownload);
        }

      return headerDownload != null;
    }

    bool TryGetPeerIdle(out Peer peer)
    {
      lock (LOCK_Peers)
        peer = Peers.Find(p => p.TryRequestIdlePeer());

      return peer != null;
    }

    async Task SyncDB(Peer peer)
    {
    }
  }
}