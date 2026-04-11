using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;

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
      Header HeaderTip;
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

      void ReleaseLockSynchronization()
      {
        lock (this)
          FlagSynchronizationLocked = false;
      }

      public bool TryExtendHeaderchain(
        Header header, 
        out List<byte[]> locator,
        ref Block blockDownload)
      {
        if (header == null || !TryLockSynchronization())
        {
          locator = null;
          return false;
        }

        try
        {
          Header headerAncestor = HeaderTip;

          while (!headerAncestor.Hash.IsAllBytesEqual(header.HashPrevious))
          {
            if (headerAncestor == HeaderRoot)
            {
              foreach (Synchronization sync in SynchronizationBranches)
                if (sync.TryExtendHeaderchain(header, out locator, ref blockDownload))
                  return true;

              locator = GetLocator();
              return false;
            }

            headerAncestor = headerAncestor.HeaderPrevious;
          }

          while (headerAncestor != HeaderTip)
          {
            if (headerAncestor.HeaderNext.Hash.IsAllBytesEqual(header.Hash) == false)
            {
              foreach (Synchronization sync in SynchronizationBranches)
                if (sync.HeaderRoot.Hash.IsAllBytesEqual(header.Hash))
                  return sync.TryExtendHeaderchain(header.HeaderNext, out locator, ref blockDownload);

              Header headerTip = header.AppendToHeader(headerAncestor);
              Synchronization syncBranch = new(header, headerTip);
              SynchronizationBranches.Add(syncBranch);

              locator = new List<byte[]> { headerTip.Hash };
              return false;
            }

            if (header.HeaderNext == null)
            {
              locator = null;
              return false;
            }

            headerAncestor = headerAncestor.HeaderNext;
            header = header.HeaderNext;
          }

          HeaderTip = header.AppendToHeader(HeaderTip);
          locator = new List<byte[]> { HeaderTip.Hash };
          return true;
        }
        finally
        {
          ReleaseLockSynchronization();
        }
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

      public void InsertBlock(ref Block block)
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
