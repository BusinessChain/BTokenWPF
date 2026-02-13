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

          IsFork = indexHeaderAncestor != 0;

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
      
      public bool TryInsertHeaders(List<Header> headers)
      {
        foreach (Header header in headers)
          if (!TryInsertHeader(header))
            return false;

        return true;
      }

      public bool IsWeakerThan(Header header)
      {
        return HeaderTip == null || HeaderTip.DifficultyAccumulated < header.DifficultyAccumulated;
      }

      Dictionary<int, Header> HeadersDownloading = new();
      Header HeaderDownloadNext;
      int HeightInsertionNext;

      object LOCK_Peers = new();
      List<Peer> Peers = new();

      public void StartSynchronization()
      {
        HeaderDownloadNext = HeaderRoot;
        HeightInsertionNext = HeaderRoot.Height;

        int heightHeaderTipOld = HeaderTip.Height;

        lock (LOCK_Peers)
          Peers.ForEach(p => p.StartBlockDownload());
      }

      object LOCK_BlockInsertion = new();
      ConcurrentBag<Block> PoolBlocks = new();
      int ID_Synchronization;

      public void InsertBlock(Block block)
      {
        int heightBlock = block.Header.Height;

        lock (LOCK_BlockInsertion)
        {
          if (TryAddBlockToQueueBlocks(heightBlock, block))
          {
            HeadersDownloading.Remove(heightBlock);

            if (GetDifficultyAccumulatedHeaderTip() > Network.GetDifficultyAccumulatedHeaderTip())
            {
              Network.Reorganize(this);
            }
          }
          else
          {
            PoolBlocks.Add(block);
          }
        }
      }

      public int GetHeightAncestor()
      {
        return HeaderRoot.HeaderPrevious.Height;
      }

      public override string ToString()
      {
        return $"{Locator.First()} ... {Locator.Last()}";
      }
    }
  }
}
