using System;
using System.IO;
using System.Linq;
using System.Threading;


namespace BTokenLib
{
  public class BlockArchiver
  {
    string PathBlockArchive;
    string PathBlockArchiveMain;
    string PathBlockArchiveFork;

    Token Token;


    public BlockArchiver(Token token)
    {
      Token = token;

      PathBlockArchiveMain = Path.Combine(token.GetName(), "blocks");
      Directory.CreateDirectory(PathBlockArchiveMain);

      PathBlockArchiveFork = Path.Combine(PathBlockArchiveMain, "fork");

      PathBlockArchive = PathBlockArchiveMain;
    }

    public void DeleteBlock(int blockHeight)
    {
      string pathBlock = Path.Combine(PathBlockArchive, blockHeight.ToString());
      File.Delete(pathBlock);
    }

    //Store tha last 400 MB if pruning is activated.
    public bool TryLoadBlock(int blockHeight, out Block block)
    {
      block = null;
      string pathBlock = Path.Combine(PathBlockArchive, blockHeight.ToString());

      while (true)
        try
        {
          block = new(Token, File.ReadAllBytes(pathBlock));
          block.Parse(blockHeight);

          return true;
        }
        catch (FileNotFoundException)
        {
          return false;
        }
        catch (IOException ex)
        {
          ($"{ex.GetType().Name} when attempting to load file {pathBlock}: {ex.Message}.\n" +
            $"Retry in {Token.TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, Token.LogEntryNotifier);

          Thread.Sleep(Token.TIMEOUT_FILE_RELOAD_SECONDS * 1000);
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when loading block height {blockHeight} from disk. Block deleted."
          .Log(this, Token.LogEntryNotifier);

          DeleteBlock(blockHeight);

          return false;
        }
    }

    public byte[] LoadBlockBytes(int blockHeight)
    {
      return File.ReadAllBytes(Path.Combine(PathBlockArchive, blockHeight.ToString()));
    }

    public void SetBlockPathToFork()
    {
      Directory.CreateDirectory(PathBlockArchiveFork);
      PathBlockArchive = PathBlockArchiveFork;
    }

    public void ResetBlockPath()
    {
      if (Directory.Exists(PathBlockArchiveFork))
        Directory.Delete(PathBlockArchiveFork, recursive: true);

      PathBlockArchive = PathBlockArchiveMain;
    }

    public void Reorganize()
    {
      if (Directory.Exists(PathBlockArchiveFork))
      {
        foreach (string pathFile in Directory.GetFiles(PathBlockArchiveFork))
        {
          string newPathFile = Path.Combine(
            PathBlockArchiveMain,
            Path.GetFileName(pathFile));

          File.Delete(newPathFile);
          File.Move(pathFile, newPathFile);
        }

        Directory.Delete(PathBlockArchiveFork, recursive: true);

        PathBlockArchive = PathBlockArchiveMain;
      }
    }

  }
}
