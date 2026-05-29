using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;

namespace BTokenLib
{
  partial class Network
  {
    class Synchronization
    {
      Synchronization SynchronizationParent;
      List<Synchronization> SynchronizationBranches = new();

      Token Token;

      Header HeaderGenesis = Token.CreateHeaderGenesis();
      Header HeaderRoot;
      public Header HeaderTip;
      Header HeaderTipBlockchain;
      int HeightBlockInsertNext;

      string PathDirectoryBlocks;

      Dictionary<byte[], Header> HeadersDownloading = new(new EqualityComparerByteArray());
      Header HeaderDownloadNext;

      const int CAPACITY_MAX_QueueBlocksInsertion = 20;
      Dictionary<int, Block> QueueBlocks = new();
      ConcurrentBag<Block> PoolBlocks = new();


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

      public Block GetBlock(byte[] hash)
      {
        Block block = null;

        int heightBlock = -1;
        byte[] buffer = null;

        Header header = HeaderRoot;

        while (header != null)
        {
          if(header.Hash.IsAllBytesEqual(hash))
          {
            string pathFile = Path.Combine(PathDirectoryBlocks, header.Height.ToString());

            try
            {
              buffer = File.ReadAllBytes(pathFile);
            }
            catch
            {
              return false;
            }

            heightBlock = header.Height;
            return true;
          }

          header = header.HeaderNext;
        }

        foreach(Synchronization syncBranch in SynchronizationBranches)
          if (TryGetBlock(hash, out buffer, ref heightBlock))
            return true;

        return false;
      }
      
      public Block GetBlock(int height)
      {
        SynchronizationRoot.TryGetBlock()
      block = null;
        string pathBlock = Path.Combine(PathBlockArchive, blockHeight.ToString());

        while (true)
          try
          {
            block = new(Token, File.ReadAllBytes(pathBlock));
            block.Parse();

            return true;
          }
          catch (FileNotFoundException)
          {
            return false;
          }
          catch (IOException ex)
          {
            ($"{ex.GetType().Name} when attempting to load file {pathBlock}: {ex.Message}.\n" +
              $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, Token.LogEntryNotifier);

            Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
          }
          catch (Exception ex)
          {
            $"{ex.GetType().Name} when loading block height {blockHeight} from disk. Block deleted."
            .Log(this, Token.LogEntryNotifier);

            File.Delete(Path.Combine(PathBlockArchive, blockHeight.ToString()));

            return false;
          }
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

      public void LoadFromDisk()
      {
        // Load initial Synchronization from Token database
        // Connect Token database.

        int heightBlockNext = Directory.GetFiles(PathBlockArchive, "*.blk")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => int.TryParse(name, out _))
        .Select(name => int.Parse(name))
        .DefaultIfEmpty(0)
        .Min();

        while (TryLoadBlock(heightBlockNext, out Block block))
          try
          {
            if (HeaderTip == null)
              HeaderTip = block.Header;
            else
              block.Header.AppendToHeader(HeaderTip);

            Token.InsertBlock(block);

            HeaderTip.HeaderNext = block.Header;
            HeaderTip = block.Header;

            heightBlockNext += 1;
          }
          catch (ProtocolException ex)
          {
            $"{ex.GetType().Name} when inserting block {block}, height {heightBlockNext} loaded from disk: \n{ex.Message}. \nBlock is deleted."
            .Log(this, LogEntryNotifier);

            File.Delete(Path.Combine(PathBlockArchive, heightBlockNext.ToString()));
          }

        HeaderTip ??= HeaderGenesis;
      }
    }
  }
}
