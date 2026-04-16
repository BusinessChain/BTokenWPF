using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;

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


      public Synchronization(Synchronization synchronizationRoot, Header headerRoot, Header headerTip)
      {
        SynchronizationRoot = synchronizationRoot;
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
      }

      public bool TryLockSynchronization()
      {
        if (FlagIsAborted)
          return false;

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
        if (!FlagIsAborted) // kann eine abortet Sync wieder belebt werden wenn neue Header kommen?
          if ((QueueBlocks.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null)
            && HeadersDownloading.Any())
            return HeadersDownloading[HeadersDownloading.Keys.Min()];
          else if (HeaderDownloadNext != null)
          {
            Header headerDownload = HeaderDownloadNext;
            HeadersDownloading.Add(headerDownload.Height, headerDownload);
            HeaderDownloadNext = HeaderDownloadNext.HeaderNext;
            return headerDownload;
          }

        return null;
      }

      public bool TryInsertBlock(Block block, out Synchronization sychronizationRoot)
      {
        sychronizationRoot = this;

        if (FlagIsAborted)
          return false;

        int heightBlock = block.Header.Height;

        try
        {
          if(!HeadersDownloading.Remove(heightBlock)) // muss mit Hash statt mit height arbeiten.
          {
            foreach (Synchronization syncBranch in SynchronizationBranches)
              if (syncBranch.TryInsertBlock(block, out sychronizationRoot))
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
                FlagIsAborted = true;
                return;
              }
            } while (QueueBlocks.TryGetValue(HeaderTipBlockchain.Height + 1, out block));

          if(HeaderTipBlockchain.DifficultyAccumulated > SynchronizationRoot?.HeaderTipBlockchain.DifficultyAccumulated)
          {
            SynchronizationRoot.TryReorgToken(this);
          }
        }
        finally
        {
          ReleaseLockSynchronization();
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
