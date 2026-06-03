using System;
using System.Linq;
using System.IO;
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

      public Header HeaderTip;
      Header HeaderGenesis;
      Header HeaderRoot;
      Header HeaderTipBlockchain;
      int HeightBlockInsertNext;

      string PathDirectoryBlocks;

      Dictionary<byte[], Header> HeadersDownloading = new(new EqualityComparerByteArray());
      Header HeaderDownloadNext;

      const int CAPACITY_MAX_QueueBlocksInsertion = 20;
      Dictionary<int, Block> QueueBlocks = new();
      ConcurrentBag<Block> PoolBlocks = new();

      Block BlockLoad;

      public Synchronization(Token token)
      {
        Token = token;

        HeaderGenesis = Token.CreateHeaderGenesis();
        HeaderTip = HeaderGenesis;

        BlockLoad = new(Token);
      }

      public Synchronization(Synchronization synchronizationRoot, Header headerRoot, Header headerTip)
      {
        SynchronizationParent = synchronizationRoot;
        HeaderRoot = headerRoot;
        HeaderTip = headerTip;
        HeightBlockInsertNext = HeaderRoot.Height;

        if (synchronizationRoot == null)
          PathDirectoryBlocks = "blocksSyncRoot";
        else
        {
          int indexBranch = synchronizationRoot.SynchronizationBranches.Count;
          PathDirectoryBlocks = Path.Combine(synchronizationRoot.PathDirectoryBlocks, $"branch{indexBranch}");
        }
      }

      public void LoadFromDisk()
      {
        int heightBlockNext = Directory.GetFiles(PathDirectoryBlocks, "*.blk")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => int.TryParse(name, out _))
        .Select(name => int.Parse(name))
        .DefaultIfEmpty(0)
        .Min();

        while (true)
          try
          {
            BlockLoad.Header = null;
            LoadBlock(heightBlockNext, BlockLoad);

            if (HeaderTip == null)
              HeaderTip = BlockLoad.Header;
            else
              BlockLoad.Header.AppendToHeader(HeaderTip);

            Token.InsertBlock(BlockLoad);

            HeaderTip.HeaderNext = BlockLoad.Header;
            HeaderTip = BlockLoad.Header;

            heightBlockNext += 1;
          }
          catch (ProtocolException ex)
          {
            $"{ex.GetType().Name} when inserting block {BlockLoad}, height {heightBlockNext} loaded from disk: \n{ex.Message}. \nBlock is deleted."
            .Log(this, Token.LogEntryNotifier);

            break;
          }
      }

      public bool TryExtendHeaderchain(Header header, out List<byte[]> locator, Block blockDownload)
      {
        locator = null;

        if (header == null)
          return false;

        Header headerAncestor = HeaderTip;

        while (!headerAncestor.Hash.IsAllBytesEqual(header.HashPrevious))
        {
          if (headerAncestor == HeaderRoot)
          {
            foreach (Synchronization sync in SynchronizationBranches)
              if (sync.TryExtendHeaderchain(header, out locator, blockDownload))
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
                return sync.TryExtendHeaderchain(header.HeaderNext, out locator, blockDownload);

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

      Header FetchHeaderDownload()
      {
        if ((QueueBlocks.Count > CAPACITY_MAX_QueueBlocksInsertion || HeaderDownloadNext == null)
            && HeadersDownloading.Any())
          return HeadersDownloading.Values.MinBy(h => h.Height);
        
        if (HeaderDownloadNext != null)
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

      public bool TryInsertBlock(ref Block block, ref Synchronization sychronizationRoot)
      {
        if (!HeadersDownloading.Remove(block.Header.Hash))
        {
          foreach (Synchronization syncBranch in SynchronizationBranches)
            if (syncBranch.TryInsertBlock(ref block, ref sychronizationRoot))
              return true;

          block.Header = null;
          return false;
        }

        int heightBlock = block.Header.Height;

        if (heightBlock == HeightBlockInsertNext)
        {
          do
          {
            try
            {
              Token?.InsertBlock(block);
            }
            catch
            {
              return false;
            }

            HeaderTipBlockchain = block.Header;

            block.WriteToDisk(PathDirectoryBlocks);

            PoolBlocks.Add(block);
          } while (QueueBlocks.TryGetValue(HeaderTipBlockchain.Height + 1, out block));

          if (TryReorg())
          {
            sychronizationRoot = this;
          }
        }
        else
          QueueBlocks.Add(heightBlock, block);

        if (!PoolBlocks.TryTake(out block))
          block = new(Token);

        block.Header = FetchHeaderDownload();

        return true;
      }

      bool TryReorg()
      {
        if (SynchronizationParent == null
          || (HeaderTipBlockchain.DifficultyAccumulated <= SynchronizationParent.HeaderTipBlockchain.DifficultyAccumulated))
          return false;

        Header headerAncestor = HeaderRoot.HeaderPrevious;

        if (SynchronizationParent.Token != null)
          if (!TryReorgToken(headerAncestor.Height))
            return false;

        PromoteSynchronization(headerAncestor);

        if (Token == null)
          return TryReorg();

        return true;
      }

      bool TryReorgToken(int heightAncestor)
      {
        SynchronizationParent.RewindTokenToHeight(heightAncestor);

        Token = SynchronizationParent.Token;

        try
        {
          RollTokenForwardToTip(heightAncestor);
        }
        catch
        {
          Token = null;

          SynchronizationParent.RollTokenForwardToTip(heightAncestor);

          return false;
        }

        SynchronizationParent.Token = null;

        return true;
      }

      void RewindTokenToHeight(int heightAncestor)
      {
        Block block = new(Token);

        int height = HeaderTip.Height;

        while(height > heightAncestor)
        {
          block.LoadFromDisk(PathDirectoryBlocks, height);

          Token.ReverseBlock(block);

          height--;
        }
      }

      void RollTokenForwardToTip(int heightAncestor)
      {
        Block block = new(Token);

        int height = heightAncestor + 1;

        while (height <= HeaderTip.Height)
        {
          block.LoadFromDisk(PathDirectoryBlocks, height);

          Token.InsertBlock(block);

          height++;
        }
      }

      void PromoteSynchronization(Header headerAncestor)
      {
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
      }

      public void GetBlock(byte[] hash, Block blockUpload)
      {
        Header header = HeaderRoot;

        while (header != null)
        {
          if(header.Hash.IsAllBytesEqual(hash))
          {
            blockUpload.Header = header;
            LoadBlock(header.Height, blockUpload);
            return;
          }

          header = header.HeaderNext;
        }

        foreach (Synchronization syncBranch in SynchronizationBranches)
        {
          syncBranch.GetBlock(hash, blockUpload);

          if (blockUpload.Header != null)
            return;
        }
      }
      
      public void LoadBlock(int height, Block blockUpload)
      {
        string pathFile = Path.Combine(PathDirectoryBlocks, height.ToString());

        using FileStream fileBlock = File.OpenRead(pathFile);

        if (fileBlock.Length > blockUpload.Buffer.Length)
          throw new InvalidOperationException("Block too large for buffer.");

        blockUpload.LengthDataPayload = (int)fileBlock.Length;

        int offset = 0;
        while (offset < blockUpload.LengthDataPayload)
        {
          int n = fileBlock.Read(
              blockUpload.Buffer,
              offset,
              blockUpload.LengthDataPayload - offset);

          if (n == 0)
            throw new EndOfStreamException();

          offset += n;
        }

        blockUpload.Parse();
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
