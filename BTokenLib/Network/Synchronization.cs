using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace BTokenLib
{
  partial class Network
  {
    partial class Synchronization
    {      
      Network Network;

      public Header HeaderTip;
      public Header HeaderRoot;

      Dictionary<int, Header> HeadersDownloading = new();
      Header HeaderDownloadNext;

      object LOCK_BlockInsertion = new();
      Dictionary<int, Block> QueueBlocks = new();
      int HeightTipQueueBlocks;
      public double DifficultyAccumulatedHeightTip;
      int HeightHeaderPopNextQueue;
      ConcurrentBag<Block> PoolBlocks = new();


      public Synchronization(Header headerRoot, Header headerTip)
      {
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
      }

      public bool TryExtendHeaderchain(Header headerRoot, Header headerTip)
      {
        while(!headerRoot.HashPrevious.IsAllBytesEqual(HeaderTip.Hash))
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
          blockDownload = new Block(Network.Token);

        return true;
      }

      public void InsertBlock(Block block)
      {
        int heightBlock = block.Header.Height;

        lock (LOCK_BlockInsertion)
        {
          HeadersDownloading.Remove(heightBlock);

          if (heightBlock <= HeightTipQueueBlocks || !QueueBlocks.TryAdd(heightBlock, block))
            return;

          if (HeightTipQueueBlocks == 0)
          {
            HeightTipQueueBlocks = heightBlock;
            DifficultyAccumulatedHeightTip += block.Header.DifficultyAccumulated;
          }
          else if (heightBlock == HeightTipQueueBlocks + 1)
            do
            {
              HeightTipQueueBlocks++;
              DifficultyAccumulatedHeightTip += block.Header.DifficultyAccumulated;
            }
            while (QueueBlocks.TryGetValue(HeightTipQueueBlocks, out block));

          Network.SynchronizeTo(this);
        }
      }

      public bool PopBlock(out Block block)
      {
        if (HeightHeaderPopNextQueue == 0)
          HeightHeaderPopNextQueue = QueueBlocks.Keys.Min();

        return QueueBlocks.Remove(HeightHeaderPopNextQueue++, out block);
      }
    }
  }
}
