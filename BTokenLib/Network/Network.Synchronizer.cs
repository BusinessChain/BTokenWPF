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
    object LOCK_BlockInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, Header> HeadersDownloading = new();
    const int TIMEOUT_BLOCKDOWNLOAD_SECONDS = 120; // If we don't get a specific block from any peers for this time, abort the synchronization process with peerSync.
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
        peer.SendGetHeaders(Token.GetLocator());
    }

    Peer PeerSync;
    HeaderchainDownload HeaderchainDownload;
    bool FlagSyncHeadersComplete;

    void ReceiveHeadersMessage(Peer peer)
    {
      lock (LOCK_IsStateSync)
        if (peer != PeerSync)
        {
          if (IsStateSync || !Token.TryLock())
            return;

          IsStateSync = true;
          PeerSync = peer;
          HeaderchainDownload = new HeaderchainDownload(Token.GetLocator());
          FlagSyncHeadersComplete = false;

          StartTimerSyncHeaders();
        }

      int startIndex = 0;
      int countHeaders = VarInt.GetInt(peer.Payload, ref startIndex);

      if (countHeaders > 0)
      {
        for (int i = 0; i < countHeaders; i += 1)
        {
          Header header = Token.ParseHeader(peer.Payload, ref startIndex, peer.SHA256);
          startIndex += 1; // Number of transaction entries, this value is always 0

          if (!HeaderchainDownload.TryInsertHeader(header))
            break;
        }

        PeerSync.SendGetHeaders(HeaderchainDownload.Locator);
      }
      else if (HeaderchainDownload.HeaderTip?.DifficultyAccumulated > Token.HeaderTip.DifficultyAccumulated)
      {
        FlagSyncHeadersComplete = true;
        SyncBlocks();
      }
    }

    const int TIMEOUT_HEADERSYNC_SECONDS = 5;
    double TimeoutAdjustementFactor = 1.0;

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
          $"Exiting state synchronization of token {Token.GetName()} due to timeout in synchronizator.".Log(this, Token.LogFile, Token.LogEntryNotifier);
          
          lock (LOCK_IsStateSync)
          {
            IsStateSync = false;
            Token.ReleaseLock();
            PeerSync.Dispose();
            PeerSync = null;
          }

          break;
        }

        await Task.Delay(1000).ConfigureAwait(false);
      }
    }

    async Task SyncBlocks()
    {
      $"Start block synchronization.".Log(this, Token.LogFile, Token.LogEntryNotifier);

      HeadersDownloading.Clear();
      QueueBlockInsertion.Clear();
      FlagSyncAbort = false;

      double difficultyAccumulatedOld = Token.HeaderTip.DifficultyAccumulated;

      HeaderDownloadNext = HeaderchainDownload.HeaderRoot;
      HeightInsertion = HeaderDownloadNext.Height;

      if (HeaderDownloadNext.HeaderPrevious != Token.HeaderTip)
      {
        $"Forking chain after common ancestor {HeaderDownloadNext.HeaderPrevious} with height {HeaderDownloadNext.HeaderPrevious.Height}."
          .Log(this, Token.LogFile, Token.LogEntryNotifier);

        if (Token.TryReverseBlockchainToHeight(HeaderchainDownload.HeaderRoot.Height - 1))
          Token.Archiver.SetBlockPathToFork();
        else
          Token.Reset(); //Restart Sync as if cold start.
      }

      int heightHeaderTipOld = Token.HeaderTip.Height;
      int countdownTimeoutBlockDownloadSeconds = 0;

      while (true)
      {
        if (Token.HeaderTip.Height == HeaderchainDownload.HeaderTip.Height)
          break;

        if (countdownTimeoutBlockDownloadSeconds > TIMEOUT_BLOCKDOWNLOAD_SECONDS)
          FlagSyncAbort = true;
        else if (heightHeaderTipOld == Token.HeaderTip.Height)
          countdownTimeoutBlockDownloadSeconds += 1;
        else
        {
          heightHeaderTipOld = Token.HeaderTip.Height;
          countdownTimeoutBlockDownloadSeconds = 0;
        }

        if (FlagSyncAbort)
        {
          $"Synchronization is aborted.".Log(this, Token.LogFile, Token.LogEntryNotifier);

          foreach (Peer p in Peers.Where(p => p.IsStateSync()))
            p.SetStateIdle();

          break;
        }

        if (TryFetchHeaderDownload(out Header headerDownload))
          if (TryGetPeerIdle(out Peer peer))
          {
            if (!PoolBlocks.TryTake(out Block blockDownload))
              blockDownload = new Block(Token);

            peer.RequestBlock(headerDownload, blockDownload);
          }

        await Task.Delay(1000).ConfigureAwait(false);
      }

      if (Token.HeaderTip.DifficultyAccumulated > difficultyAccumulatedOld)
        Token.Archiver.Reorganize();
      else
        Token.LoadState();

      lock (LOCK_IsStateSync)
      {
        Token.ReleaseLock();
        IsStateSync = false;
        PeerSync = null;

        $"Synchronization of {Token.GetName()} completed.".Log(this, Token.LogFile, Token.LogEntryNotifier);
      }

      foreach (Token tokenChild in Token.TokensChild)
        tokenChild.Network.StartSync();
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