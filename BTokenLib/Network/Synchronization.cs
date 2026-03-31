using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace BTokenLib
{
  partial class Network
  {
    class Synchronization
    {
      Synchronization SynchronizationRoot;
      List<Synchronization> SynchronizationBranches = new();

      Token Token;

      Header HeaderRoot;
      public Header HeaderTip;
      Header HeaderTipBlockchain;

      Dictionary<int, Header> HeadersDownloading = new();
      Header HeaderDownloadNext;

      const int CAPACITY_MAX_QueueBlocksInsertion = 20;
      Dictionary<int, Block> QueueBlocks = new();
      ConcurrentBag<Block> PoolBlocks = new();

      public bool FlagIsAborted;

      bool FlagSynchronizationLocked;


      public Synchronization(Header headerRoot, Header headerTip)
      {
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
      }

      bool TryLockSynchronization()
      {
        int randomTimeout = Random.Shared.Next(5, 10);

        while (randomTimeout > 0)
        {
          lock (this)
            if (!FlagSynchronizationLocked)
            {
              FlagSynchronizationLocked = true;
              return true;
            }

          Thread.Sleep(10);
          randomTimeout -= 1;
        }

        return false;
      }

      void ReleaseLockSynchronizations()
      {
        lock (this)
          FlagSynchronizationLocked = false;
      }

      public Synchronization GetSynchronization(Header header)
      {
        return GetSynchronization(header, flagLockSynchronization: true);
      }

      Synchronization GetSynchronization(Header header, bool flagLockSynchronization)
      {
        if (flagLockSynchronization && !TryLockSynchronization())
          return null;

        try
        {
          Header headerAncestor = HeaderTip;

          while (!headerAncestor.Hash.IsAllBytesEqual(header.HashPrevious))
          {
            if (headerAncestor == HeaderRoot)
            {
              foreach (Synchronization syncBranch in SynchronizationBranches)
              {
                if (syncBranch.GetSynchronization(header, flagLockSynchronization: false))
                  return true;

                return false;
              }
            }

            headerAncestor = headerAncestor.HeaderPrevious;
          }

          while (headerAncestor.HeaderNext?.Hash.IsAllBytesEqual(header.Hash) == true)
          {
            headerAncestor = headerAncestor.HeaderNext;
            header = header.HeaderNext;

            if (header == null)
              return false;
          }

          if (headerAncestor == HeaderTip)
          {
            while (header != null)
            {
              header.AppendToHeader(HeaderTip);
              HeaderTip = header;
              header = header.HeaderNext;
            }

            return this;
          }
          else
          {
            Synchronization syncBranch = SynchronizationBranches
              .Find(s => s.HeaderRoot.Hash.IsAllBytesEqual(header.Hash));

            if (syncBranch != null)
            {
              Header headerRoot = header.HeaderNext;

              if (headerRoot != null)
                return syncBranch.GetSynchronization(headerRoot, flagLockSynchronization: false);
              else
                return null;
            }
            else
            {
              Header headerRoot = header;
              Header headerTip = headerAncestor;

              while (header != null)
              {
                header.AppendToHeader(headerTip);
                headerTip = header;
                header = header.HeaderNext;
              }

              Synchronization sync = new(headerRoot, headerTip);
              SynchronizationBranches.Add(sync);
              return sync;
            }
          }
        }
        finally
        {
          if (flagLockSynchronization)
            ReleaseLockSynchronizations();
        }
      }

      public bool TryMerge(Synchronization sync)
      {
        Header headerRootSync = sync.HeaderRoot;

        while (!headerRootSync.HashPrevious.IsAllBytesEqual(HeaderTip.Hash))
        {
          headerRootSync = headerRootSync.HeaderNext;

          if (headerRootSync == null)
            return false;
        }

        headerRootSync.AppendToHeader(HeaderTip);
        HeaderTip.HeaderNext = headerRootSync;
        HeaderTip = sync.HeaderTip;

        return true;
      }

      public bool TryFetchBlockDownload(out Header headerDownload, out Block blockDownload)
      {
        headerDownload = null;
        blockDownload = null;

        if (FlagIsAborted)
          return false;

        lock (LOCK_BlockInsertion)
          if ((QueueBlocks.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null)
            && HeadersDownloading.Any())
            headerDownload = HeadersDownloading[HeadersDownloading.Keys.Min()];
          else if (HeaderDownloadNext != null)
          {
            headerDownload = HeaderDownloadNext;
            HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
            HeadersDownloading.Add(headerDownload.Height, headerDownload);
          }

        if (headerDownload == null)
          return false;

        if (!PoolBlocks.TryTake(out blockDownload))
          blockDownload = new Block(Token);

        return true;
      }

      public void InsertBlock(Block block)
      {
        int heightBlock = block.Header.Height;

        if (FlagIsAborted || !TryLockSynchronization())
          return;

        try
        {
          HeadersDownloading.Remove(heightBlock);

          if (QueueBlocks.TryAdd(heightBlock, block))
          {
            if (heightBlock == HeaderRoot.Height 
              || heightBlock == HeaderTipBlockchain?.Height + 1)
              do
              {
                HeaderTipBlockchain = block.Header;

                try
                {
                  Token?.InsertBlock(block);
                }
                catch
                {
                  FlagIsAborted = true;
                  return;
                }
              } while (QueueBlocks.TryGetValue(HeaderTipBlockchain.Height + 1, out block));
          }
          else
            PoolBlocks.Add(block);
        }
        finally
        {
          ReleaseLockSynchronizations();
        }

        if (TryLockSynchronizations())
        {
          if (SynchronizationRoot.TryReorgToken(sync))
            SynchronizationRoot = sync;

          foreach (Synchronization syncInProgress in SynchronizationsInProgress)
            if (!syncInProgress.IsHeaderTipStrongerThanBlockTip(SynchronizationRoot))
              syncInProgress.FlagIsAborted = true;

          ReleaseLockSynchronizations();
        }
      }
          
      public bool IsHeaderTipStrongerThanBlockTip(Synchronization sync)
      {
        return HeaderTip.DifficultyAccumulated >
          sync.HeaderTipBlockchain.DifficultyAccumulated;
      }

      public bool TryReorgToken(Synchronization sync)
      {
        if (HeaderTipBlockchain.DifficultyAccumulated 
          < sync.HeaderTipBlockchain.DifficultyAccumulated)
        {
          sync.Token = Token;

          if (TryRewindToHeight(sync.GetHeightAncestor())
            && sync.TryRollForwardToTip())
          {
            Token = null;
            return true;
          }

          sync.Token = null;
          sync.FlagIsAborted = true;
        }

        return false;
      }

      public List<byte[]> GetLocator()
      {
        Header header = HeaderTip;
        List<byte[]> locator = new();
        int depth = 0;
        int nextLocationDepth = 0;

        while (header != null)
        {
          if (depth == nextLocationDepth || header.HeaderPrevious == null)
          {
            locator.Add(header.Hash);
            nextLocationDepth = 2 * nextLocationDepth + 1;
          }

          depth++;
          header = header.HeaderPrevious;
        }

        return locator;
      }
    }
  }
}
