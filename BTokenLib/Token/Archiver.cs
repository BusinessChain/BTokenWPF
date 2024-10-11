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
    const int COUNT_MAX_BLOCKS_ARCHIVED = 2016;

    Token Token;


    public BlockArchiver(Token token)
    {
      Token = token;

      PathBlockArchiveMain = Path.Combine(token.GetName(), "blocks");
      Directory.CreateDirectory(PathBlockArchiveMain);

      PathBlockArchiveFork = Path.Combine(PathBlockArchiveMain, "fork");

      PathBlockArchive = PathBlockArchiveMain;
    }

    public bool TryLoadBlock(int blockHeight, out Block block)
    {
      block = null;
      string pathBlockArchive = Path.Combine(PathBlockArchive, blockHeight.ToString());

      while (true)
        try
        {
          byte[] fileBlockMined = File.ReadAllBytes(pathBlockArchive);
          block = Token.ParseBlock(fileBlockMined);
          return true;
        }
        catch (FileNotFoundException)
        {
          return false;
        }
        catch (IOException ex)
        {
          ($"{ex.GetType().Name} when attempting to load file {pathBlockArchive}: {ex.Message}.\n" +
            $"Retry in {Token.TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, Token.LogEntryNotifier);

          Thread.Sleep(Token.TIMEOUT_FILE_RELOAD_SECONDS * 1000);
        }
        catch (Exception ex)
        {
            $"{ex.GetType().Name} when trying to load block height {blockHeight} from archive."
            .Log(this, Token.LogEntryNotifier);

          return false;
        }
    }

    public bool TryLoadBlockBytes(int blockHeight, out byte[] buffer)
    {
      buffer = null;

      string pathBlockArchive = Path.Combine(PathBlockArchive, blockHeight.ToString());

      while (true)
      {
        try
        {
          buffer = File.ReadAllBytes(pathBlockArchive);
          return true;
        }
        catch (FileNotFoundException)
        {
          return false;
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when attempting to load file {pathBlockArchive}: {ex.Message}.\n" +
            $"Retry in {Token.TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, Token.LogEntryNotifier);

          Thread.Sleep(Token.TIMEOUT_FILE_RELOAD_SECONDS);
        }
      }
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

    public void ArchiveBlock(Block block)
    {
      string pathFile = Path.Combine(
        PathBlockArchive, 
        block.Header.Height.ToString());

      while (true)
        try
        {
          using (FileStream fileStreamBlock = new(
          pathFile,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
          {
            block.Serialize(fileStreamBlock);
          }

          File.Delete(Path.Combine(
            PathBlockArchive,
            (block.Header.Height - COUNT_MAX_BLOCKS_ARCHIVED).ToString()));

          break;
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when writing block height {block.Header.Height} to file:\n" +
            $"{ex.Message}\n " +
            $"Try again in {Token.TIMEOUT_FILE_RELOAD_SECONDS} seconds ...")
            .Log(this, Token.LogEntryNotifier);

          Thread.Sleep(Token.TIMEOUT_FILE_RELOAD_SECONDS);
        }
    }

  }
}
