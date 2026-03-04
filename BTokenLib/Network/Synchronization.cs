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
      Header HeaderTipBlockchain;

      Dictionary<int, Header> HeadersDownloading = new();
      Header HeaderDownloadNext;

      object LOCK_BlockInsertion = new();
      const int CAPACITY_MAX_QueueBlocksInsertion = 20;
      Dictionary<int, Block> QueueBlocks = new();
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
          HeadersDownloading.Remove(heightBlock);

          if (QueueBlocks.TryAdd(heightBlock, block))
          {
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
          }
          else
            PoolBlocks.Add(block);
        }
      }
          
      public bool IsHeaderTipStrongerThanBlockTip(Synchronization sync)
      {
        return HeaderTip.DifficultyAccumulated >
          sync.HeaderTipBlockchain.DifficultyAccumulated;
      }

      public bool TryReorgToken(Synchronization sync)
      {
        if (HeaderTipBlockchain.DifficultyAccumulated < sync.HeaderTipBlockchain.DifficultyAccumulated)
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
    }
  }
}
