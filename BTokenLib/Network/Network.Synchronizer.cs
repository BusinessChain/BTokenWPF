using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

using LiteDB;


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

    ConcurrentBag<Block> PoolBlocks = new();

    object LOCK_ChargeHashDB = new();

    Peer PeerSync;
    HeaderchainDownload HeaderchainDownloadInstance;
    bool FlagSyncHeadersExit;
    const int TIMEOUT_HEADERSYNC_SECONDS = 5;
    double TimeoutAdjustementFactor = 1.0;

    int HeightInsertionNext;
    object LOCK_BlockInsertion = new();
    const int CAPACITY_MAX_QueueBlocksInsertion = 20;
    Dictionary<int, Block> QueueBlockInsertion = new();
    Dictionary<int, Header> HeadersDownloading = new();
    const int TIMEOUT_BLOCKDOWNLOAD_SECONDS = 120;
    Header HeaderDownloadNext;

    bool FlagSyncBlocksExit;

    public string PathBlockArchive;
    public string PathBlockArchiveMain = "PathBlockArchiveMain";
    public string PathBlockArchiveFork = "PathBlockArchiveFork";
    public string PathFileHeaderchain;

    public const int TIMEOUT_FILE_RELOAD_SECONDS = 10;


    List<Header> GetLocator()
    {
      Header header = HeaderTip;
      List<Header> locator = new();
      int depth = 0;
      int nextLocationDepth = 0;

      while (header != null)
      {
        if (depth == nextLocationDepth || header.HeaderPrevious == null)
        {
          locator.Add(header);
          nextLocationDepth = 2 * nextLocationDepth + 1;
        }

        depth++;
        header = header.HeaderPrevious;
      }

      return locator;
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
          HeaderchainDownload = new HeaderchainDownload(GetLocator());
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
      else if (HeaderchainDownload.IsStrongerThan())
      {
        if(HeaderchainDownload.IsFork)
          if (!TryReverseBlockchainToHeight(HeaderchainDownload.GetHeightAncestor()))
          {
            HeaderchainDownload = new HeaderchainDownload(GetLocator());
            PeerSync.SendHeaders(HeaderchainDownload.Locator);
            return;
          }

        FlagSyncHeadersExit = true;
        StartSynchronizationBlocks();
      }
      else
      {
        lock (LOCK_IsStateSync)
        {
          PeerSync = null;
          IsStateSync = false;
          Token.ReleaseLock();
        }

        if (HeaderchainDownload.IsWeakerThan(HeaderTip))
          PeerSync.SendHeaders(GetLocator());
      }
    }

    bool TryReverseBlockchainToHeight(int heightBlockAncestor)
    {
      int heightBlock = Directory.GetFiles(PathBlockArchive)
        .Select(f => Path.GetFileName(f))
        .Select(name => int.TryParse(name, out int value) ? value : -1)
        .DefaultIfEmpty(-1)
        .Max();

      while (heightBlockAncestor < heightBlock && TryLoadBlock(heightBlock, out Block block))
        if (Token.TryReverseBlock(block))
          heightBlock -= 1;

      if (heightBlockAncestor != heightBlock)
        return false;

      if (PathBlockArchive == PathBlockArchiveMain)
      {
        Directory.CreateDirectory(PathBlockArchiveFork);
        PathBlockArchive = PathBlockArchiveFork;
      }
      else if (PathBlockArchive == PathBlockArchiveFork)
      {
        Directory.Delete(PathBlockArchiveFork, recursive: true);
        PathBlockArchive = PathBlockArchiveMain;
      }

      return true;
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

    void StartSynchronizationBlocks(HeaderchainDownload headerchainDownload)
    {
      // hier einen Entscheidungsmechanismus einbauen um zu schauen,ob überhaupt stärker is als main branch,
      // und ob eine bereits laufende synchronisation unterbrochen und geswitched werden soll. 
      lock (LOCK_IsStateSync)
        if (IsStateSync)
          return;
        else
          IsStateSync = true;

      // Evt. ein Synchrinzer objekt einführen, welches instanziert wird.
      // Allenfalls kann HeaderDownload und Synchronizer zu einem Objekt verschmolzen werden.
      HeaderchainDownloadInstance = headerchainDownload;

      HeadersDownloading.Clear();
      QueueBlockInsertion.Clear();
      HeaderDownloadNext = HeaderchainDownloadInstance.HeaderRoot;
      HeightInsertionNext = HeaderDownloadNext.Height;
      FlagSyncBlocksExit = false;

      int heightHeaderTipOld = HeaderTip.Height;

      lock (LOCK_Peers)
        Peers.ForEach(p => p.StartBlockDownload());

      // beim insert des letztes blockes wird der Reorg getriggert.
      // Ein Timeout gibt es nicht. Im Prinzip wird einfach ewig versucht zu syncen.
      // Sollte jedoch während der Sync eine stärkere Headerchain bekannt werden, 
      // wird auf diese umgesynct. Beim syncen die DB nicht manipulieren, alles nur
      // mit Cache machen. Tiefe reorgs werden abgelehnt, müssen manuell getriggert werden.


      if (headerchainDownload.IsFork)
      {
        if (HeaderTip.DifficultyAccumulated > headerchainDownload.HeaderTipTokenInitial.DifficultyAccumulated)
          Reorganize();
        else
          TryReverseBlockchainToHeight(headerchainDownload.GetHeightAncestor());
      }
    }

    void InsertBlock(Block block)
    {
      int heightBlock = block.Header.Height;

      lock (LOCK_BlockInsertion)
      {
        if (heightBlock < HeightInsertionNext || !QueueBlockInsertion.TryAdd(heightBlock, block))
        {
          PoolBlocks.Add(block);
          return;
        }

        HeadersDownloading.Remove(heightBlock);

        // Vielleicht ist der Block mit HeightInsertionNext nicht da, muss auch ok sein.
        while (QueueBlockInsertion.Remove(HeightInsertionNext, out block))
        {
          // Allenfalls kann IsFork dynamisch auf false gesetzt werden nach dem reorg.
          // HeaderchainDownload und Syncer zu einem Objekt zusammen ist, kann IsFork immer 
          // gerade anzeigen wo wir sind.
          if (HeaderchainDownloadInstance.IsFork && HeightInsertionNext <= HeaderTip.Height + 1)
          {
            block.WriteToDisk(PathBlockArchiveFork);
          }
          else
          {
            block.WriteToDisk(PathBlockArchiveMain);

            try
            {
              block.Header.AppendToHeader(HeaderTip);

              Token.InsertBlock(block);

              HeaderTip.HeaderNext = block.Header;
              HeaderTip = HeaderTip.HeaderNext;

              DatabaseHeaderCollection.Upsert(new BsonDocument
              {
                ["_id"] = block.Header.Hash,
                ["buffer"] = block.Header.Serialize()
              });
            }
            catch (Exception ex)
            {
              $"Abort Sync. Insertion of block {block} failed:\n {ex.Message}.".Log(this, Token.LogFile, Token.LogEntryNotifier);

              File.Delete(pathBlockTemp);

              FlagSyncBlocksExit = true;
              return;
            }
          }

          PoolBlocks.Add(block);

          HeightInsertionNext += 1;
        }

        if (HeaderTip.Height == HeaderchainDownloadInstance.HeaderTip.Height)
        {
          Log($"Downloading blocks completed.");
          FlagSyncBlocksExit = true;
        }
      }
    }

    bool TryFetchHeaderDownload(out Header headerDownload)
    {
      headerDownload = null;

      lock (LOCK_BlockInsertion)
        if ((QueueBlockInsertion.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null)
          && HeadersDownloading.Any())
          headerDownload = HeadersDownloading[HeadersDownloading.Keys.Min()];
        else if (HeaderDownloadNext != null)
        {
          headerDownload = HeaderDownloadNext;
          HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
          HeadersDownloading.Add(headerDownload.Height, headerDownload);
        }

      return headerDownload != null;
    }

    public void Reorganize()
    {
      foreach (string pathFile in Directory.GetFiles(PathBlockArchiveFork))
      {
        string newPathFile = Path.Combine(PathBlockArchiveMain, Path.GetFileName(pathFile));

        File.Delete(newPathFile);
        File.Move(pathFile, newPathFile);
      }

      Directory.Delete(PathBlockArchiveFork, recursive: true);
      PathBlockArchive = PathBlockArchiveMain;
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