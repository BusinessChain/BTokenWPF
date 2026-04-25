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
      Synchronization SynchronizationParent;
      List<Synchronization> SynchronizationBranches = new();

      Token Token;

      Header HeaderRoot;
      Header HeaderTip;
      Header HeaderTipBlockchain;

      Dictionary<byte[], Header> HeadersDownloading = new(new EqualityComparerByteArray());
      Header HeaderDownloadNext;

      const int CAPACITY_MAX_QueueBlocksInsertion = 20;
      Dictionary<int, Block> QueueBlocks = new();
      ConcurrentBag<Block> PoolBlocks = new();

      bool FlagSynchronizationLocked;


      public Synchronization(Synchronization synchronizationRoot, Header headerRoot, Header headerTip)
      {
        SynchronizationParent = synchronizationRoot;
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
      }

      public bool TryLockSynchronization()
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
        locator = null;

        if (blockDownload == null)
          blockDownload = new(Token);

        blockDownload.Header = null;

        if (header == null || !TryLockSynchronization())
          return false;

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
              Synchronization syncBranch = new(this, header, headerTip);
              SynchronizationBranches.Add(syncBranch);

              blockDownload.Header = syncBranch.FetchHeaderDownload();
              locator = new List<byte[]> { headerTip.Hash };
              return false;
            }

            if (header.HeaderNext == null)
            {
              blockDownload.Header = FetchHeaderDownload();
              locator = null;
              return false;
            }

            headerAncestor = headerAncestor.HeaderNext;
            header = header.HeaderNext;
          }

          blockDownload.Header = FetchHeaderDownload();
          HeaderTip = header.AppendToHeader(HeaderTip);
          locator = new List<byte[]> { HeaderTip.Hash };
          return true;
        }
        finally
        {
          ReleaseLockSynchronization();
        }
      }

      Header FetchHeaderDownload()
      {
        if ((QueueBlocks.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null)
            && HeadersDownloading.Any())
          return HeadersDownloading.Values.MinBy(h => h.Height);
        else if (HeaderDownloadNext != null)
        {
          Header headerDownload = HeaderDownloadNext;
          HeadersDownloading.Add(headerDownload.Hash, headerDownload);
          HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
          return headerDownload;
        }

        return null;
      }

      Synchronization GetSynchronizationRoot()
      {
        if (SynchronizationParent == null)
          return this;

        return SynchronizationParent.GetSynchronizationRoot();
      }

      public bool TryInsertBlock(Block block, ref Synchronization sychronizationRoot)
      {
        int heightBlock = block.Header.Height;
        if (!HeadersDownloading.Remove(block.Header.Hash))
        {
          foreach (Synchronization syncBranch in SynchronizationBranches)
            if (syncBranch.TryInsertBlock(block, ref sychronizationRoot))
              return true;

          block.Header = null;
          return false;
        }

        QueueBlocks.Add(heightBlock, block);

        if (heightBlock == HeaderRoot.Height || heightBlock == HeaderTipBlockchain?.Height + 1)
          do
          {
            HeaderTipBlockchain = block.Header;

            try
            {
              Token?.InsertBlock(block);
            }
            catch
            {
              return false;
            }
          } while (QueueBlocks.TryGetValue(HeaderTipBlockchain.Height + 1, out block));

        if (TryReorg())
          sychronizationRoot = this;

        return true;
      }

      bool TryReorg()
      {
        if (SynchronizationParent == null ||
          (SynchronizationParent.HeaderTipBlockchain.DifficultyAccumulated >= HeaderTipBlockchain.DifficultyAccumulated))
        {
          return false;
        }

        Header headerAncestor = HeaderRoot.HeaderPrevious;

        if (SynchronizationParent.Token != null)
        {
          SynchronizationParent.RewindTokenToHeight(headerAncestor.Height);

          Token = SynchronizationParent.Token;

          try
          {
            RollTokenForwardToTip();
          }
          catch
          {
            Token = null;

            SynchronizationParent.RollTokenForwardToTip();

            return false;
          }

          SynchronizationParent.Token = null;
        }

        Header headerRootNewSyncParent = headerAncestor.HeaderNext;
        headerAncestor.HeaderNext = HeaderRoot;
        HeaderRoot = SynchronizationParent.HeaderRoot;
        SynchronizationParent.HeaderRoot = headerRootNewSyncParent;

        List<Synchronization> branches = SynchronizationParent.SynchronizationBranches.ToList();

        foreach (Synchronization syncBranch in branches)
          if (syncBranch.HeaderRoot.Height <= HeaderRoot.Height)
          {
            SynchronizationParent.SynchronizationBranches.Remove(syncBranch);

            if (syncBranch != this)
            {
              syncBranch.SynchronizationParent = this;
              SynchronizationBranches.Add(syncBranch);
            }
          }

        SynchronizationBranches.Add(SynchronizationParent);

        Synchronization syncParentNew = SynchronizationParent.SynchronizationParent;
        SynchronizationParent.SynchronizationParent = this;
        SynchronizationParent = syncParentNew;

        if (Token != null)
          return true;

        return TryReorg();
      }

      public bool IsHeaderTipStrongerThanBlockTip(Synchronization sync)
      {
        return HeaderTip.DifficultyAccumulated >
          sync.HeaderTipBlockchain.DifficultyAccumulated;
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
