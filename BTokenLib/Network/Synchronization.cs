using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace BTokenLib
{
  partial class Network
  {
    class Synchronization
    {
      Token Token;

      Header HeaderRoot;
      Header HeaderTip;

      Dictionary<int, Header> HeadersDownloading = new();
      Header HeaderDownloadNext;

      object LOCK_BlockInsertion = new();
      const int CAPACITY_MAX_QueueBlocksInsertion = 20;
      Dictionary<int, Block> QueueBlocks = new();
      Header HeaderTipBlockchain;
      double DifficultyAccumulated;
      ConcurrentBag<Block> PoolBlocks = new();

      public bool FlagIsAborted;


      public Synchronization(Header headerRoot, Header headerTip)
      {
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
      }

      public bool TryExtendHeaderchain(Header headerRoot, Header headerTip)
      {
        while (!headerRoot.HashPrevious.IsAllBytesEqual(HeaderTip.Hash))
        {
          headerRoot = headerRoot.HeaderNext;

          if (headerRoot == null)
            return false;
        }

        headerRoot.AppendToHeader(HeaderTip);
        HeaderTip.HeaderNext = headerRoot;
        HeaderTip = headerTip;

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

        lock (LOCK_BlockInsertion)
        {
          if (!QueueBlocks.TryAdd(heightBlock, block))
          {
            PoolBlocks.Add(block);
            return;
          }

          HeadersDownloading.Remove(heightBlock);

          if (heightBlock == HeaderRoot.Height || heightBlock == HeaderTipBlockchain?.Height + 1)
            do
            {
              try
              {
                Token?.InsertBlock(block);
              }
              catch
              {
                FlagIsAborted = true;
                return;
              }

              HeaderTipBlockchain = block.Header;
            } while (QueueBlocks.TryGetValue(HeaderTipBlockchain.Height + 1, out block));
        }
      }

      public bool IsStrongerThan(Synchronization synchronization)
      {
        return DifficultyAccumulated > synchronization.DifficultyAccumulated;
      }

      public bool TrySwitchSynchronizationWith(Synchronization synchronization)
      {
        if (DifficultyAccumulated < synchronization.DifficultyAccumulated)
        {
          synchronization.Token = Token;

          if (TryRewindToHeight(synchronization.GetHeightAncestor())
            && synchronization.TryRollForwardToTip())
          {
            Token = null;
            return true;
          }

          synchronization.Token = null;
          synchronization.FlagIsAborted = true;
        }

        return false;
      }
    }
  }
}
