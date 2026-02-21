using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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

    object LOCK_ChargeHashDB = new();

    Peer PeerSync;
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

    List<Synchronization> SynchronizationsInProgress = new();

    Synchronization ReceiveHeaderchain(Header headerRoot, Header headerTip)
    {
      lock (SynchronizationsInProgress)
      {
        if (headerTip.DifficultyAccumulated <= SynchronizationsInProgress[0].DifficultyAccumulatedHeightTip)
          return null;

        foreach (Synchronization syncInProgress in SynchronizationsInProgress)
          if (syncInProgress.TryExtendHeaderchain(headerRoot, headerTip))
            return syncInProgress;

        Synchronization syncNew = new(headerRoot, headerTip);
        SynchronizationsInProgress.Add(syncNew);
        return syncNew;
      }
    }

    void InsertBlock(Block block)
    {
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

    bool SynchronizeTo(Synchronization synchronization)
    {
      lock (Lock_StateNetwork)
      {
        if (synchronization.DifficultyAccumulatedHeightTip > HeaderTip.DifficultyAccumulated)
        {
          RewindToHeight(synchronization.HeaderRoot.Height);

          // if anything fails here, restore blockchain
          while (synchronization.PopBlock(out Block block))
            InsertBlock(block); 
        }

        // Switch "synchronization"

      }



      ///Alt
      foreach (string pathFile in Directory.GetFiles(PathBlockArchiveFork))
      {
        string newPathFile = Path.Combine(PathBlockArchiveMain, Path.GetFileName(pathFile));

        File.Delete(newPathFile);
        File.Move(pathFile, newPathFile);
      }

      Directory.Delete(PathBlockArchiveFork, recursive: true);
      PathBlockArchive = PathBlockArchiveMain;
    }

    bool TryConnectHeaderToChain(Header header)
    {
      lock (Lock_StateNetwork)
      {
        Header headerInChain = HeaderTip;

        do
        {
          if (header.HashPrevious.IsAllBytesEqual(headerInChain.Hash))
          {
            header.AppendToHeader(headerInChain);
            return true;
          }

          headerInChain = headerInChain.HeaderPrevious;
        } while (headerInChain != null);

        return false;
      }
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