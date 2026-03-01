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

    bool TryReceiveHeaderchain(
      Header headerRoot, Header headerTip, 
      out Synchronization sync)
    {
      sync = null;

      lock (SynchronizationsInProgress)
      {
        foreach(Synchronization syncInProgress in SynchronizationsInProgress)
        {
          if (syncInProgress == SynchronizationsInProgress.First()
            && syncInProgress.DifficultyAccumulated >= headerTip.DifficultyAccumulated)
            return false;

          if (syncInProgress.TryExtendHeaderchain(headerRoot, headerTip))
          {
            sync = syncInProgress;
            return true;
          }
        }

        sync = new(headerRoot, headerTip);
        SynchronizationsInProgress.Add(sync);
        return true;
      }
    }

    readonly object LOCK_SynchronizationLocal = new object(); 
    Synchronization SynchronizationLocal;

    void SynchronizeTo(Synchronization synchronization)
    {
      lock (LOCK_SynchronizationLocal) 
        if (/*zu tiefer height Ancestor*/)
        {
          synchronization.FlagIsAborted = true;
          Log($"Can not sync to Synchronization because fork height too deep.");
        }
        else if (SynchronizationLocal.DifficultyAccumulated < synchronization.DifficultyAccumulated)
        {
          SynchronizationLocal.RewindToHeight(synchronization.GetHeightAncestor());
          SynchronizationLocal.TransferDatabaseTo(synchronization);
          SynchronizationLocal = synchronization;
          SynchronizationLocal.RollForwardToTip();
        }
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

    async Task SyncDB(Peer peer)
    {
    }
  }
}