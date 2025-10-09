using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using LiteDB;


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

    const int COUNT_MAX_BYTES_IN_BLOCK_ARCHIVE = 400_000_000; // Read from configuration file
    const int COUNT_MAX_ACCOUNTS_IN_CACHE = 5_000_000; // Read from configuration file
    const double HYSTERESIS_COUNT_MAX_CACHE_ARCHIV = 0.9;

    public Wallet Wallet;

    public Network Network;

    public StreamWriter LogFile;
    public ILogEntryNotifier LogEntryNotifier;

    public const int TIMEOUT_FILE_RELOAD_SECONDS = 10;

    bool IsLocked;
    static object LOCK_Token = new();

    // Kann man das vielleicht im BToken machen, weil Bitcoin braucht das ja nicht, oder?
    protected LiteDatabase LiteDatabase;
    protected ILiteCollection<BsonDocument> DatabaseMetaCollection;
    protected ILiteCollection<BsonDocument> DatabaseHeaderCollection;


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

      LiteDatabase = new LiteDatabase($"{GetName()}.db;Mode=Exclusive");
      DatabaseHeaderCollection = LiteDatabase.GetCollection<BsonDocument>("headers");
      DatabaseMetaCollection = LiteDatabase.GetCollection<BsonDocument>("meta");

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

      Wallet.Clear();
    }

    public void Start()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = TokenParent;

      token.LoadCache();
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

    protected void LoadCache()
    {
      Reset();

      LoadHeaderTip();

      LoadBlocksFromArchive();

      TokensChild.ForEach(t => t.LoadCache());
    }

    void LoadHeaderTip()
    {
      HeaderTip = HeaderGenesis;

      byte[] hash;
      int height;
      byte[] headerBuffer = null;

      try
      {
        hash = DatabaseMetaCollection.FindById("lastProcessedBlock")["hash"].AsBinary;
        height = DatabaseMetaCollection.FindById("lastProcessedBlock")["height"].AsInt32;

        if (hash.IsAllBytesEqual(HeaderGenesis.Hash))
          return;

        headerBuffer = DatabaseHeaderCollection.FindById(hash)["buffer"].AsBinary;

        SHA256 sHA256 = SHA256.Create();
        Header header = null;
        int startIndex = 0;

        header = ParseHeader(headerBuffer, ref startIndex, sHA256);
        header.Height = height;

        header.CountBytesTXs = BitConverter.ToInt32(headerBuffer, startIndex);
        startIndex += 4;

        int countHashesChild = VarInt.GetInt(headerBuffer, ref startIndex);

        for (int i = 0; i < countHashesChild; i++)
        {
          byte[] iDToken = new byte[IDToken.Length];
          Array.Copy(headerBuffer, startIndex, iDToken, 0, iDToken.Length);
          startIndex += iDToken.Length;

          byte[] hashesChild = new byte[32];
          Array.Copy(headerBuffer, startIndex, hashesChild, 0, 32);
          startIndex += 32;

          header.HashesChild.Add(iDToken, hashesChild);
        }

        HeaderTip = header;
      }
      catch (Exception ex)
      {
        ($"Exception {ex.GetType().Name} thrown when trying to load headerTip from database: \n{ex.Message}\n" +
          $"Set HeaderTip to HeaderGenesis.").Log(this, LogEntryNotifier);

        DatabaseMetaCollection.Upsert(new BsonDocument
        {
          ["_id"] = "lastProcessedBlock",
          ["hash"] = HeaderGenesis.Hash,
          ["height"] = 0
        });
      }
    }

    void LoadBlocksFromArchive()
    {
      int heightBlockNext = HeaderTip.Height + 1;

      while (TryLoadBlock(heightBlockNext, out Block block))
        try
        {
          $"Load block {block}.".Log(this, LogEntryNotifier);

          block.Header.AppendToHeader(HeaderTip);

          InsertBlockInDatabase(block);

          HeaderTip.HeaderNext = block.Header;
          HeaderTip = block.Header;

          // Muss idempotent sein, da diese Blöcke schon beim ursprünglichen Cache insert in die Wallet DB eingeführt wurden.
          Wallet.InsertBlock(block);
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when inserting block {block}, height {heightBlockNext} loaded from disk: \n{ex.Message}. \nBlock is deleted."
          .Log(this, LogEntryNotifier);

          File.Delete(Path.Combine(PathBlockArchive, heightBlockNext.ToString()));
        }
    }

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

    public bool TryReverseCacheToHeight(int height)
    {
      int heightDatabase = DatabaseMetaCollection.FindById("lastProcessedBlock")["height"].AsInt32;

      if (heightDatabase > height)
        return false;

      while (height < HeaderTip.Height && TryLoadBlock(HeaderTip.Height, out Block block))
        try
        {
          ReverseBlockInCache(block);

          Wallet.ReverseBlock(block);

          DatabaseHeaderCollection.Delete(HeaderTip.Hash);

          HeaderTip = HeaderTip.HeaderPrevious;
          HeaderTip.HeaderNext = null;
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when reversing block {block}, height {HeaderTip.Height} loaded from disk: \n{ex.Message}."
          .Log(this, LogEntryNotifier);

          break;
        }

      if (height == HeaderTip.Height)
      {
        if (PathBlockArchive == PathBlockArchiveMain)
        {
          Directory.CreateDirectory(PathBlockArchiveFork);
          PathBlockArchive = PathBlockArchiveFork;
        }
        else if (PathBlockArchive == PathBlockArchiveFork)
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
      LoadCache();

      return false;
    }

    public virtual bool TryStageBlock(Block block)
    {
      return true;
    }

    public void InsertBlock(Block block)
    {
      $"Insert block {block}.".Log(this, LogEntryNotifier);

      block.Header.AppendToHeader(HeaderTip);

      InsertBlockInDatabase(block);

      HeaderTip.HeaderNext = block.Header;
      HeaderTip = block.Header;

      Wallet.InsertBlock(block);

      TXPool.RemoveTXs(block.TXs.Select(tX => tX.Hash), FileTXPoolBackup);
      TXsPoolBackup.RemoveAll(tXPool => block.TXs.Any(tXBlock => tXPool.Hash.IsAllBytesEqual(tXBlock.Hash)));

      DeleteBlocksMinedUnconfirmed();

      foreach (var hashBlockChildToken in block.Header.HashesChild)
        TokensChild.Find(t => t.IDToken.IsAllBytesEqual(hashBlockChildToken.Key))?
          .InsertBlockMined(hashBlockChildToken.Value);
    }

    /*
       * Das Netwerk speichert die letzten Zig Blöcke. Das ganze Blockarchive geht ins Netzwerk.
       * Im Netzwerk wird zuerst der Block gespeichert, dann Token.InsertBlock(block) gemacht, wenn exception, dann Block wieder löschen. 
       * In Token.InsertBlock(block) innen drin Cache auf Disk dumpen, falls Cache zu gross wird
       * und entsprechend Headerchain nachgeführen.
       * Wenn Block Archiv überläuft, wird das per Token.AnnounceDumpBlock(HeightBlock) gemeldet, dann wird auch auf disk gedumpt und headerchain nachgeführt.
       * Es wird angenommen dass im DB Cache bereits vermekt ist, welche Transaktion bei welcher height kreiert wurde und was entsprechend gemacht
       * werden muss bei dumpDisk oder Rollback. Der */

    protected virtual void InsertBlockInDatabase(Block block)
    { }

    public virtual void InsertBlockMined(byte[] hashBlock)
    { throw new NotImplementedException(); }

    protected virtual void ReverseBlockInCache(Block block) { }
                  
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

    public bool TryGetHeader(byte[] headerHash, out Header header)
    {
      header = null;

      int key = BitConverter.ToInt32(headerHash, 0);

      lock (IndexHeaders)
        if (IndexHeaders.TryGetValue(key, out List<Header> headers))
          header = headers.FirstOrDefault(h => headerHash.IsAllBytesEqual(h.Hash));

      return header != null;
    }
  }
}
