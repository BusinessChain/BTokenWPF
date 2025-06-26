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

    Peer PeerSync;
    HeaderchainDownload HeaderchainDownload;
    bool FlagSyncHeadersExit;
    const int TIMEOUT_HEADERSYNC_SECONDS = 5;
    double TimeoutAdjustementFactor = 1.0;

    int HeightInsertion;
    object LOCK_BlockInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, Header> HeadersDownloading = new();
    const int TIMEOUT_BLOCKDOWNLOAD_SECONDS = 120;
    Header HeaderDownloadNext;

    bool FlagSyncBlocksExit;


    public void StartSync()
    {
      $"Try start synchronization of token {Token.GetName()}. Send getheaders to all peers."
        .Log(this, Token.LogFile, Token.LogEntryNotifier);

      if (IsStateSync)
        return;

      foreach(Peer peer in Peers)
        peer.SendGetHeaders(Token.GetLocator());
    }

    void TryReceiveHeaders(Peer peer, List<Header> headers, ref bool flagMessageMayNotFollowConsensusRules)
    {
      lock (LOCK_IsStateSync)
        if (peer != PeerSync)
        {
          if (IsStateSync || !Token.TryLock())
            return;

          IsStateSync = true;
          PeerSync = peer;
          HeaderchainDownload = new HeaderchainDownload(Token.GetLocator());
          FlagSyncHeadersExit = false;

          StartTimerSyncHeaders();
        }
        else if (FlagSyncHeadersExit)
          return;

      if (headers.Any() && HeaderchainDownload.TryInsertHeaders(headers))
      {
        flagMessageMayNotFollowConsensusRules = false;
        PeerSync.SendGetHeaders(HeaderchainDownload.Locator);
      }
      else if (HeaderchainDownload.IsStrongerThan(Token.HeaderTip))
      {
        if(HeaderchainDownload.IsFork)
          if (!Token.TryReverseBlockchainToHeight(HeaderchainDownload.GetHeightAncestor()))
          {
            HeaderchainDownload = new HeaderchainDownload(Token.GetLocator());
            PeerSync.SendHeaders(HeaderchainDownload.Locator);
            return;
          }

        FlagSyncHeadersExit = true;
        SyncBlocks();
      }
      else
      {
        lock (LOCK_IsStateSync)
        {
          PeerSync = null;
          IsStateSync = false;
          Token.ReleaseLock();
        }

        if (HeaderchainDownload.IsWeakerThan(Token.HeaderTip))
          PeerSync.SendHeaders(Token.GetLocator());
      }
    }

    async Task StartTimerSyncHeaders()
    {
      Header headerTipOld = HeaderchainDownload.HeaderTip;
      int countdownTimeoutSeconds = 0;

      while (!FlagSyncHeadersExit)
      {
        if (countdownTimeoutSeconds < TIMEOUT_HEADERSYNC_SECONDS * TimeoutAdjustementFactor)
        {
          if (headerTipOld == HeaderchainDownload.HeaderTip)
            countdownTimeoutSeconds += 1;
          else
          {
            headerTipOld = HeaderchainDownload.HeaderTip;
            countdownTimeoutSeconds = 0;
          }
        }
        else
        {
          $"Exiting header synchronization of token {Token.GetName()} due to timeout in synchronizator.".Log(this, Token.LogFile, Token.LogEntryNotifier);
          
          lock (LOCK_IsStateSync)
          {
            PeerSync = null;
            IsStateSync = false;
            Token.ReleaseLock();
          }

          break;
        }

        await Task.Delay(1000).ConfigureAwait(false);
      }
    }

    async Task SyncBlocks()
    {
      HeadersDownloading.Clear();
      QueueBlockInsertion.Clear();
      HeaderDownloadNext = HeaderchainDownload.HeaderRoot;
      HeightInsertion = HeaderDownloadNext.Height;
      FlagSyncBlocksExit = false;

      int heightHeaderTipOld = Token.HeaderTip.Height;
      int countdownTimeoutSeconds = 0;

      while (!FlagSyncBlocksExit)
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


        if (countdownTimeoutSeconds < TIMEOUT_BLOCKDOWNLOAD_SECONDS * 10 * TimeoutAdjustementFactor)
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
          FlagSyncBlocksExit = true;
          break;
        }

        await Task.Delay(100).ConfigureAwait(false);
      }

      Token.Reorganize(
        HeaderchainDownload.HeaderTipTokenOld.DifficultyAccumulated,
        HeaderchainDownload.HeaderRoot.HeaderPrevious.Height);

      lock (LOCK_IsStateSync)
      {
        Token.ReleaseLock();
        IsStateSync = false;
        PeerSync = null;
      }

      $"Synchronization of {Token.GetName()} completed.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      Peer peerNextSync = Peers.Find(p => p.HeightHeaderTipLastCommunicated != Token.HeaderTip.Height);

      if (peerNextSync != null)
        peerNextSync.SendGetHeaders(Token.GetLocator());
      else
        Token.TokensChild.ForEach(t => t.StartSync());
    }

    void InsertBlock(Peer peer, ref bool flagMessageMayNotFollowConsensusRules)
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
              FlagSyncBlocksExit = true;
              return;
            }

            PoolBlocks.Add(block);

            HeightInsertion += 1;

          } while (QueueBlockInsertion.Remove(HeightInsertion, out block));

        if (Token.HeaderTip.Height >= HeaderchainDownload.HeaderTip.Height)
        {
          $"Downloading blocks completed.".Log(this, Token.LogFile, Token.LogEntryNotifier);
          FlagSyncBlocksExit = true;
        }
      }
    }

    bool TryFetchHeaderDownload(out Header headerDownload)
    {
      headerDownload = null;

      lock (LOCK_FetchHeaderDownload) // Statt den queue abfragen auf count, einfacher den HeadersDownloading abfragen. Wobei der queue muss so oder so abgefragt werden
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