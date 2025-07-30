using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public abstract partial class Token
  {
    public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' };

    public byte[] IDToken;

    public Token TokenParent;
    public List<Token> TokensChild = new();

    public Header HeaderGenesis;
    public Header HeaderTip;
    Dictionary<int, List<Header>> IndexHeaders = new();

    public long BlockRewardInitial;
    public int PeriodHalveningBlockReward;

    public int SizeBlockMax;

    public TXPool TXPool;
    public FileStream FileTXPoolBackup;
    public List<TX> TXsPoolBackup = new();

    public string PathBlockArchive;
    public string PathBlockArchiveMain = "PathBlockArchiveMain";
    public string PathBlockArchiveFork = "PathBlockArchiveFork";
    public string PathFileHeaderchain;

    public Wallet Wallet;

    public Network Network;

    public StreamWriter LogFile;
    public ILogEntryNotifier LogEntryNotifier;

    public const int TIMEOUT_FILE_RELOAD_SECONDS = 10;

    bool IsLocked;
    static object LOCK_Token = new();


    public Token(UInt16 port, byte[] iDToken, bool flagEnableInboundConnections, ILogEntryNotifier logEntryNotifier)
    {
      IDToken = iDToken;

      Directory.CreateDirectory(GetName());

      LogFile = new StreamWriter(Path.Combine(GetName(), "LogToken"), append: false);

      LogEntryNotifier = logEntryNotifier;

      FileTXPoolBackup = new FileStream(
        Path.Combine(GetName(), "FileTXPoolBackup"),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read);

      HeaderGenesis = CreateHeaderGenesis();

      Network = new(this, port, flagEnableInboundConnections);
    }

    public Token(
      UInt16 port,
      byte[] iDToken,
      bool flagEnableInboundConnections,
      ILogEntryNotifier logEntryNotifier,
      Token tokenParent)
      : this(
          port, 
          iDToken, 
          flagEnableInboundConnections, 
          logEntryNotifier)
    {
      TokenParent = tokenParent;
      TokenParent.TokensChild.Add(this);
      HeaderGenesis.HeaderParent = TokenParent.HeaderGenesis;
      TokenParent.HeaderGenesis.HashesChild.Add(IDToken, HeaderGenesis.Hash);
    }

    public virtual void Reset()
    {
      if (Directory.Exists(PathBlockArchiveFork))
        Directory.Delete(PathBlockArchiveFork, recursive: true);

      PathBlockArchive = PathBlockArchiveMain;

      HeaderTip = HeaderGenesis;
      IndexHeaders.Clear();
      IndexingHeaderTip();

      Wallet.Clear();
    }

    public void Start()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = TokenParent;

      token.LoadState();
      token.LoadTXPool();
      token.StartNetwork();
    }

    void StartNetwork()
    {
      new Thread(Network.Start).Start(); // evt. kein Thread machen, da alles async ist.

      TokensChild.ForEach(t => t.StartNetwork());
    }

    public string GetStatus()
    {
      string messageStatus = "";

      var ageBlock = TimeSpan.FromSeconds(
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - HeaderTip.UnixTimeSeconds);

      messageStatus +=
        $"Height: {HeaderTip.Height}\n" +
        $"Block tip: {HeaderTip.Hash.ToHexString().Substring(0, 24) + " ..."}\n" +
        $"Difficulty Tip: {HeaderTip.Difficulty}\n" +
        $"Acc. Difficulty: {HeaderTip.DifficultyAccumulated}\n" +
        $"Timestamp: {DateTimeOffset.FromUnixTimeSeconds(HeaderTip.UnixTimeSeconds)}\n" +
        $"Age: {ageBlock}\n";

      return messageStatus;
    }

    public abstract List<string> GetSeedAddresses();

    public void StartSync()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = token.TokenParent;

      token.Network.StartSync();
    }

    public bool TryLock()
    {
      if (TokenParent != null)
        return TokenParent.TryLock();

      lock (LOCK_Token)
      {
        if (IsLocked)
          return false;

        IsLocked = true;
        return true;
      }
    }

    public void ReleaseLock()
    {
      if (TokenParent == null)
        IsLocked = false;
      else
        TokenParent.ReleaseLock();
    }

    public abstract Header CreateHeaderGenesis();

    public void LoadState()
    {
      Reset();

      LoadImageHeaderchain();

      LoadBlocksFromArchive();

      TokensChild.ForEach(t => t.LoadState());
    }

    public abstract void LoadImageHeaderchain();

    public abstract void LoadBlocksFromArchive();

    public void LoadTXPool()
    {
      SHA256 sHA256 = SHA256.Create();

      byte[] fileData = new byte[FileTXPoolBackup.Length];
      FileTXPoolBackup.Read(fileData, 0, (int)FileTXPoolBackup.Length);

      int startIndex = 0;
      while(startIndex < fileData.Length)
      {
        int indexTxStart = startIndex;

        TX tX = ParseTX(fileData, ref startIndex, sHA256);

        tX.TXRaw = new byte[startIndex - indexTxStart];

        Array.Copy(fileData, indexTxStart, tX.TXRaw, 0, tX.TXRaw.Length);

        if (TXPool.TryAddTX(tX))
          Wallet.InsertTXUnconfirmed(tX);
      }

      TokensChild.ForEach(t => t.LoadTXPool());
    }

    public async Task RebroadcastTXsUnconfirmed()
    {
      // Rebroadcast wird bei beendigung der Netzwerk Sync getriggert.
      // Versuche alle txs die noch nicht bestätigt wurden zu rebroadcasten
    }

    public void Reorganize()
    {
      foreach (string pathFile in Directory.GetFiles(PathBlockArchiveFork))
      {
        string newPathFile = Path.Combine(PathBlockArchiveMain, Path.GetFileName(pathFile));

        File.Delete(newPathFile);
        File.Move(pathFile, newPathFile);
      }

      Directory.Delete(PathBlockArchiveFork, recursive: true);
      PathBlockArchive = PathBlockArchiveMain;
    }

    public abstract void InsertBlockInDatabase(Block block);

    public void InsertBlock(Block block)
    {
      $"Insert block {block}.".Log(this, LogEntryNotifier);

      block.Header.AppendToHeader(HeaderTip);

      InsertBlockInDatabase(block);

      HeaderTip.HeaderNext = block.Header;
      HeaderTip = block.Header;

      IndexingHeaderTip();

      Wallet.InsertBlock(block);

      TXPool.RemoveTXs(block.TXs.Select(tX => tX.Hash), FileTXPoolBackup);
      TXsPoolBackup.RemoveAll(tXPool => block.TXs.Any(tXBlock => tXPool.Hash.IsAllBytesEqual(tXBlock.Hash)));

      DeleteBlocksMinedUnconfirmed();

      foreach (var hashBlockChildToken in block.Header.HashesChild)
        TokensChild.Find(t => t.IDToken.IsAllBytesEqual(hashBlockChildToken.Key))?
          .InsertBlockMined(hashBlockChildToken.Value);
    }

    public virtual void InsertBlockMined(byte[] hashBlock)
    { throw new NotImplementedException(); }


    // Beim Reversen einfach den cache droppen und neu laden bis zur gewünschten höhe.
    // Wenn zu einer height geladen werden soll, welche tiefer als der cache ist, wird direkt 
    // die Fork DB geladen, zuerst MerkleRoot dan testen ob hash mit DB-Hash im header übereinstimmt.
    // Falls ja, wird neue DB geladen, während alte noch bewahrt wird, bis gültigkeit der neuen bestätigt ist.
    // Später kann man evt. mit diff. File arbeiten.
    public bool TryReverseBlockchainToHeight(int height)
    {
      // Droppe einfach die aktuelle Cache DB und versuche auf Height zu builden
      // testen ob height tiefer ist als Cache.

      List<Block> blocksReversed = new();
      List<TX> tXsPoolBackup = TXsPoolBackup.ToList();

      TXsPoolBackup.Clear();
      TXPool.Clear();
      Wallet.ClearTXsUnconfirmed();

      while (height < HeaderTip.Height && TryLoadBlock(HeaderTip.Height, out Block block))
        try
        {
          ReverseBlockInDB(block);

          Wallet.ReverseBlock(block);

          RemoveIndexHeaderTip();

          HeaderTip = HeaderTip.HeaderPrevious;
          HeaderTip.HeaderNext = null;

          blocksReversed.Add(block);
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when reversing block {block}, height {HeaderTip.Height} loaded from disk: \n{ex.Message}."
          .Log(this, LogEntryNotifier);

          break;
        }

      if (height == HeaderTip.Height)
      {
        //blocksReversed.Reverse();

        //foreach (Block block in blocksReversed)
        //  foreach (TX tX in block.TXs)
        //    InsertTXUnconfirmed(tX);

        //foreach (TX tX in tXsPoolBackup)
        //  InsertTXUnconfirmed(tX);

        if(PathBlockArchive == PathBlockArchiveMain)
        {
          Directory.CreateDirectory(PathBlockArchiveFork);
          PathBlockArchive = PathBlockArchiveFork;
        }
        else if(PathBlockArchive == PathBlockArchiveFork)
        {
          Directory.Delete(PathBlockArchiveFork, recursive: true);
          PathBlockArchive = PathBlockArchiveMain;
        }

        return true;
      }

      $"Failed to reverse blockchain to Height. \nReload state.".Log(this, LogFile, LogEntryNotifier);

      // Soll hier auch neu synchronisiert werden? Ja, wann immer meine DB korrupt
      // ist, komplett neu aufsynchronisiert. Allerdings wird zuerst versucht das 
      // lokale Blockarchiv 
      LoadState();

      return false;
    }

    public abstract void ReverseBlockInDB(Block block);
                  
    public abstract Header ParseHeader(byte[] buffer, ref int index, SHA256 sHA256);

    public TX ParseTX(byte[] tXRaw, SHA256 sHA256)
    {
      int startIndex = 0;

      TX tX = ParseTX(tXRaw, ref startIndex, sHA256);

      tX.TXRaw = tXRaw;

      return tX;
    }

    public abstract TX ParseTX(byte[] buffer, ref int index, SHA256 sHA256);
    
    public abstract TX ParseTXCoinbase(byte[] buffer, ref int index, SHA256 sHA256, long blockReward);

    public bool IsMining;

    public void StopMining()
    {
      IsMining = false;
    }

    public void StartMining()
    {
      if (IsMining)
        return;

      IsMining = true;

      $"Start {GetName()} miner".Log(this, LogFile, LogEntryNotifier);

      new Thread(RunMining).Start();
    }

    abstract protected void RunMining();

    //Store tha last 400 MB if pruning is activated.
    public bool TryLoadBlock(int blockHeight, out Block block)
    {
      block = null;
      string pathBlock = Path.Combine(PathBlockArchive, blockHeight.ToString());

      while (true)
        try
        {
          block = new(this, File.ReadAllBytes(pathBlock));
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
            $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when loading block height {blockHeight} from disk. Block deleted."
          .Log(this, LogEntryNotifier);

          File.Delete(Path.Combine(PathBlockArchive, blockHeight.ToString()));

          return false;
        }
    }

    public bool TryGetBlockBytes(byte[] hash, out byte[] buffer)
    {
      buffer = null;

      if (TryGetHeader(hash, out Header header))
        try
        {
          buffer = File.ReadAllBytes(Path.Combine(PathBlockArchive, header.Height.ToString()));
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when loading block {hash.ToHexString()} from disk.".Log(this, LogEntryNotifier);
        }

      return buffer != null;
    }

    public virtual void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    { throw new NotImplementedException(); }

    public virtual void DeleteDB()
    { throw new NotImplementedException(); }

    public virtual void DeleteBlocksMinedUnconfirmed() { }

    public virtual List<byte[]> ParseHashesDB(byte[] buffer, int length, Header headerTip)
    { throw new NotImplementedException(); }

    public string GetName()
    {
      return GetType().Name;
    }

    public virtual bool TryGetDB(byte[] hash, out byte[] dataDB)
    { throw new NotImplementedException(); }

    public void InsertTXUnconfirmed(TX tX)
    {
      if (TXPool.TryAddTX(tX))
      {
        tX.WriteToStream(FileTXPoolBackup);
        FileTXPoolBackup.Flush();

        TXsPoolBackup.Add(tX);

        Wallet.InsertTXUnconfirmed(tX);
        Wallet.AddTXUnconfirmedToHistory(tX);
      }
      else
        $"Could not insert tX {tX} to pool.".Log(this, LogEntryNotifier);
    }

    public bool TrySendTX(string address, long value, double feePerByte, out TX tX)
    {
      if (Wallet.TryCreateTX(address, value, feePerByte, out tX))
      {
        InsertTXUnconfirmed(tX);
        Network.AdvertizeTX(tX);
        return true;
      }

      return false;
    }

    public bool TryBroadcastTXData(byte[] data, double feeSatoshiPerByte, int sequence = 0)
    {
      if (Wallet.TryCreateTXData(data, sequence, feeSatoshiPerByte, out TX tX))
      {
        InsertTXUnconfirmed(tX);
        Network.AdvertizeTX(tX);

        $"Created and broadcasted anchor token {tX}.".Log(this, LogEntryNotifier);

        return true;
      }

      return false;
    }

    public List<Header> GetLocator()
    {
      Header header = HeaderTip;
      List<Header> locator = new();
      int depth = 0;
      int nextLocationDepth = 0;

      while (header != null)
      {
        if (depth == nextLocationDepth || header.HeaderPrevious == null)
        {
          locator.Add(header);
          nextLocationDepth = 2 * nextLocationDepth + 1;
        }

        depth++;
        header = header.HeaderPrevious;
      }

      return locator;
    }

    public List<Header> GetHeaders(IEnumerable<byte[]> locatorHashes, int count, byte[] stopHash)
    {
      foreach (byte[] hash in locatorHashes)
      {
        if (TryGetHeader(hash, out Header header))
        {
          List<Header> headers = new();

          while (
            header.HeaderNext != null &&
            headers.Count < count &&
            !header.Hash.IsAllBytesEqual(stopHash))
          {
            Header nextHeader = header.HeaderNext;

            headers.Add(nextHeader);
            header = nextHeader;
          }

          return headers;
        }
      }

      throw new ProtocolException(string.Format(
        "Locator does not root in headerchain."));
    }

    public bool TryGetHeader(byte[] headerHash, out Header header)
    {
      header = null;

      int key = BitConverter.ToInt32(headerHash, 0);

      lock (IndexHeaders)
        if (IndexHeaders.TryGetValue(key, out List<Header> headers))
          header = headers.FirstOrDefault(h => headerHash.IsAllBytesEqual(h.Hash));

      return header != null;
    }

    protected void IndexingHeaderTip()
    {
      int keyHeader = BitConverter.ToInt32(HeaderTip.Hash, 0);

      lock (IndexHeaders)
      {
        if (!IndexHeaders.TryGetValue(keyHeader, out List<Header> headers))
        {
          headers = new List<Header>();
          IndexHeaders.Add(keyHeader, headers);
        }

        headers.Add(HeaderTip);
      }
    }

    void RemoveIndexHeaderTip()
    {
      int keyHeader = BitConverter.ToInt32(HeaderTip.Hash, 0);

      lock (IndexHeaders)
        if (IndexHeaders.TryGetValue(BitConverter.ToInt32(HeaderTip.Hash, 0), out List<Header> headers))
          headers.RemoveAll(h => h.Hash.IsAllBytesEqual(HeaderTip.Hash));
    }
  }
}
