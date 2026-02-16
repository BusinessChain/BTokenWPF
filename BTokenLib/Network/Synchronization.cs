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

      public List<Header> Locator;

      public Header HeaderTipTokenInitial;

      public Header HeaderTip;
      public Header HeaderRoot;

      public bool IsFork;

      Dictionary<int, Header> HeadersDownloading = new();
      Header HeaderDownloadNext;
      int HeightInsertionNext;

      object LOCK_Peers = new();
      List<Peer> Peers = new();

      object LOCK_BlockInsertion = new();
      ConcurrentBag<Block> PoolBlocks = new();
      int HeightTipQueueBlocks;
      public double DifficultyAccumulatedHeightTip;
      Dictionary<int, Block> QueueBlocks = new();


      public Synchronization(Peer peer, List<Header> locator)
      {
        Network = peer.Network;
        Peers.Add(peer);
        Locator = locator;
        HeaderTipTokenInitial = locator.First();
      }

      public bool TryInsertHeader(Header header)
      {
        if (Locator.Any(h => h.Hash.IsAllBytesEqual(header.Hash)))
          return false;

        if (HeaderRoot == null)
        {
          int indexHeaderAncestor = Locator.FindIndex(h => h.Hash.IsAllBytesEqual(header.HashPrevious));

          if (indexHeaderAncestor == -1)
            return false;

          IsFork = indexHeaderAncestor > 0;

          Header headerAncestor = Locator[indexHeaderAncestor];

          if (headerAncestor.HeaderNext?.Hash.IsAllBytesEqual(header.Hash) == true)
            headerAncestor = headerAncestor.HeaderNext;
          else
          {
            header.AppendToHeader(headerAncestor);
            HeaderRoot = header;
            HeaderTip = header;

            Locator = Locator.Skip(indexHeaderAncestor).ToList();
            Locator.Insert(0, header);
          }
        }
        else if (header.HashPrevious.IsAllBytesEqual(HeaderTip.Hash))
        {
          header.AppendToHeader(HeaderTip);
          HeaderTip.HeaderNext = header;
          HeaderTip = header;

          Locator[0] = header;
        }
        else return false;

        return true;
      }
      
      public void InsertHeaders(List<Header> headers)
      {
        foreach (Header header in headers)
          if (!TryInsertHeader(header))
            break;
      }

      public bool IsWeakerThan(Header header)
      {
        return HeaderTip == null || HeaderTip.DifficultyAccumulated < header.DifficultyAccumulated;
      }

      public void Start()
      {
        HeaderDownloadNext = HeaderRoot;
        HeightInsertionNext = HeaderRoot.Height;

        int heightHeaderTipOld = HeaderTip.Height;

        lock (LOCK_Peers)
          Peers.ForEach(p => StartBlockDownload(p));
      }

      bool TryFetchHeaderDownload(out Header headerDownload)
      {
        headerDownload = null;

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

        return headerDownload != null;
      }

      void StartBlockDownload(Peer peer)
      {
        if (!TryFetchHeaderDownload(out Header headerDownload))
          return;

        if (!PoolBlocks.TryTake(out Block blockDownload))
          blockDownload = new Block(Network.Token);

        peer.StartBlockDownload(headerDownload, blockDownload);
      }

      public void InsertBlock(Block block, Peer peer)
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

        StartBlockDownload(peer);
      }


      int HeightHeaderPopNextQueue;

      public bool PopBlock(out Block block)
      {
        if (HeightHeaderPopNextQueue == 0)
          HeightHeaderPopNextQueue = QueueBlocks.Keys.Min();

        return QueueBlocks.Remove(HeightHeaderPopNextQueue++, out block);
      }

      public override string ToString()
      {
        return $"{Locator.First()} ... {Locator.Last()}";
      }
    }
  }
}
