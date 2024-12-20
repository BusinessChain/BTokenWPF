﻿using System;
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
    public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' }; // wird nirgens geprüft

    public byte[] IDToken;

    public Token TokenParent;
    public List<Token> TokensChild = new();

    public Header HeaderGenesis;
    public Header HeaderTip;
    Dictionary<int, List<Header>> HeaderIndex = new();

    public long BlockRewardInitial;
    public int PeriodHalveningBlockReward;

    public int SizeBlockMax;

    public TXPool TXPool;
    public FileStream FileTXPoolBackup;

    public BlockArchiver Archiver;

    public Wallet Wallet;

    public Network Network;

    public StreamWriter LogFile;

    const string NameImage = "Image";
    const string NameImageOld = "ImageOld";

    string PathImage;
    string PathImageOld;
    string PathRootToken;

    public const int TIMEOUT_FILE_RELOAD_SECONDS = 10;

    const int INTERVAL_BLOCKHEIGHT_IMAGE = 50;

    bool IsLocked;
    static object LOCK_Token = new();

    public ILogEntryNotifier LogEntryNotifier;


    public Token(
      UInt16 port,
      byte[] iDToken,
      bool flagEnableInboundConnections,
      ILogEntryNotifier logEntryNotifier)
    {
      IDToken = iDToken;
      LogEntryNotifier = logEntryNotifier;

      PathRootToken = GetName();
      Directory.CreateDirectory(PathRootToken);

      LogFile = new StreamWriter(
        Path.Combine(GetName(), "LogToken"),
        append: false);

      FileTXPoolBackup = new FileStream(
        Path.Combine(GetName(), "FileTXPoolBackup"),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read);

      PathImage = Path.Combine(PathRootToken, NameImage);
      PathImageOld = Path.Combine(PathRootToken, NameImageOld);

      HeaderGenesis = CreateHeaderGenesis();
      HeaderTip = HeaderGenesis;

      IndexingHeaderTip();

      Archiver = new(this);

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

    public void Start()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = TokenParent;

      token.LoadImage();
      token.LoadTXPool();
      token.StartNetwork();
    }

    void StartNetwork()
    {
      new Thread(Network.Start).Start(); // evt. kein Thread machen, da alles async ist.

      TokensChild.ForEach(t => t.StartNetwork());
    }

    public void PrintImage(ref string text)
    {
      try
      {
        if (TokenParent != null)
          TokenParent.PrintImage(ref text);

        text += $"\nPrint Image {GetName()}.\n";

        string pathHeaderchain = Path.Combine(GetName(), NameImage, "ImageHeaderchain");

        byte[] bytesHeaderImage = File.ReadAllBytes(pathHeaderchain);

        text += $"Loaded image headerchain {pathHeaderchain}.\n";

        int index = 0;
        int heightHeader = 0;

        SHA256 sHA256 = SHA256.Create();

        while (index < bytesHeaderImage.Length)
        {
          Header header = ParseHeader(bytesHeaderImage, ref index, sHA256);

          heightHeader += 1;

          header.CountBytesTXs = BitConverter.ToInt32(bytesHeaderImage, index);

          index += 4;

          text += $"{heightHeader}, {header}";

          byte flagHasHashChild = bytesHeaderImage[index];
          index += 1;

          if (flagHasHashChild == 0x01)
          {
            //header.HashesChild = new byte[32];
            //Array.Copy(bytesHeaderImage, index, header.HashesChild, 0, 32);
            //index += 32;

            //text += $" -> {header.HashesChild.ToHexString()}";
          }

          text += "\n";
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine($"Exception {ex.GetType().Name} when printing image.");
      }
    }

    public void PrintBlocks(ref string text)
    {
      if (TokenParent != null)
        TokenParent.PrintBlocks(ref text);

      text += $"\nPrint blocks {GetName()}.\n";

      int i = 1;

      while (Archiver.TryLoadBlock(i, out Block block))
      {
        text += $"{i} -> {block.Header}\n";

        //if(TokenChild != null)
        //  foreach (TX tX in block.TXs)
        //    text += tX.Print();

        i++;
      }
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

    public void LoadImage()
    {
      Reset();

      string pathImage = Path.Combine(GetName(), NameImage);

      while (true)
      {
        try
        {
          $"Load image of token {pathImage}.".Log(this, LogFile, LogEntryNotifier);

          LoadImageHeaderchain(pathImage);
          LoadImageDatabase(pathImage);
          Wallet.LoadImage(pathImage);

          break;
        }
        catch
        {
          Reset();

          if (Directory.Exists(pathImage))
            Directory.Delete(pathImage, recursive: true);

          try
          {
            Directory.Move(Path.Combine(GetName(), NameImageOld), pathImage);
          }
          catch (DirectoryNotFoundException)
          {
            break;
          }
        }
      }

      LoadBlocksFromArchive();

      TokensChild.ForEach(t => t.LoadImage());
    }

    void LoadBlocksFromArchive()
    {
      int heightBlock = HeaderTip.Height + 1;

      while (Archiver.TryLoadBlock(heightBlock, out Block block))
      {
        try
        {
          block.Header.AppendToHeader(HeaderTip);

          InsertBlockInDB(block);

          HeaderTip.HeaderNext = block.Header;
          HeaderTip = block.Header;

          IndexingHeaderTip();

          heightBlock += 1;
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when inserting block {block}, height {heightBlock} loaded from disk: \n{ex.Message}. \nBlock is deleted."
          .Log(this, LogEntryNotifier);

          Archiver.DeleteBlock(heightBlock);
        }
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

    public virtual void Reset()
    {
      Archiver.ResetBlockPath();

      HeaderTip = HeaderGenesis;
      HeaderIndex.Clear();
      IndexingHeaderTip();

      Wallet.Clear();
    }

    public void LoadImageHeaderchain(string pathImage)
    {
      byte[] bytesHeaderImage = File.ReadAllBytes(
        Path.Combine(pathImage, "ImageHeaderchain"));

      int index = 0;

      $"Load headerchain of {GetName()}.".Log(this, LogFile, LogEntryNotifier);

      SHA256 sHA256 = SHA256.Create();

      while (index < bytesHeaderImage.Length)
      {
        Header header = ParseHeader(bytesHeaderImage, ref index, sHA256);

        header.CountBytesTXs = BitConverter.ToInt32(bytesHeaderImage, index);
        index += 4;

        int countHashesChild = VarInt.GetInt(bytesHeaderImage, ref index);
        
        for(int i = 0; i < countHashesChild; i++)
        {
          byte[] iDToken = new byte[IDToken.Length];
          Array.Copy(bytesHeaderImage, index, iDToken, 0, iDToken.Length);
          index += iDToken.Length;

          byte[] hashesChild = new byte[32];
          Array.Copy(bytesHeaderImage, index, hashesChild, 0, 32);
          index += 32;

          header.HashesChild.Add(iDToken, hashesChild);
        }

        header.AppendToHeader(HeaderTip);

        HeaderTip.HeaderNext = header;
        HeaderTip = header;

        IndexingHeaderTip();
      }
    }

    public abstract void LoadImageDatabase(string path);

    public abstract void InsertBlockInDB(Block block);

    public void InsertBlock(Block block)
    {
      $"Insert block {block} in {this}.".Log(this, LogEntryNotifier);

      block.Header.AppendToHeader(HeaderTip);

      InsertBlockInDB(block);

      HeaderTip.HeaderNext = block.Header;
      HeaderTip = block.Header;

      IndexingHeaderTip();

      TXPool.RemoveTXs(block.TXs.Select(tX => tX.Hash), FileTXPoolBackup);

      Archiver.ArchiveBlock(block);

      DeleteBlocksMinedUnconfirmed();

      if (block.Header.Height % INTERVAL_BLOCKHEIGHT_IMAGE == 0)
        CreateImage();

      foreach (var hashBlockChildToken in block.Header.HashesChild)
        TokensChild.Find(t => t.IDToken.IsAllBytesEqual(hashBlockChildToken.Key))?
          .InsertBlockMined(hashBlockChildToken.Value);
    }

    public virtual void InsertBlockMined(byte[] hashBlock)
    { throw new NotImplementedException(); }

    public bool TryReverseBlockchainToHeight(int height)
    {
      string pathImage = Path.Combine(GetName(), NameImage);

      while (height < HeaderTip.Height && Archiver.TryLoadBlock(HeaderTip.Height, out Block block))
      {
        try
        {
          ReverseInDB(block);

          // Reverse transaction to TXPool

          RemoveIndexHeaderTip();

          HeaderTip = HeaderTip.HeaderPrevious;
          HeaderTip.HeaderNext = null;
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when reversing block {block}, height {HeaderTip.Height} loaded from disk: \n{ex.Message}."
          .Log(this, LogEntryNotifier);

          break;
        }
      }

      return height == HeaderTip.Height;
    }

    public abstract void ReverseInDB(Block block);

    public void Reorganize()
    {
      Archiver.Reorganize();
    }

    public void CreateImage()
    {
      PathImage.TryMoveDirectoryTo(PathImageOld);

      Directory.CreateDirectory(PathImage);

      CreateImageHeaderchain(PathImage);

      CreateImageDatabase(PathImage);

      Wallet.CreateImage(PathImage);
    }

    void CreateImageHeaderchain(string pathImage)
    {
      using (FileStream fileImageHeaderchain = new(
          Path.Combine(pathImage, "ImageHeaderchain"),
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
      {
        Header header = HeaderGenesis.HeaderNext;

        while (header != null)
        {
          byte[] headerBytes = header.Serialize();

          fileImageHeaderchain.Write(headerBytes);

          fileImageHeaderchain.Write(BitConverter.GetBytes(header.CountBytesTXs));

          fileImageHeaderchain.Write(VarInt.GetBytes(header.HashesChild.Count));

          foreach(var hashChild in header.HashesChild)
          {
            fileImageHeaderchain.Write(hashChild.Key);
            fileImageHeaderchain.Write(hashChild.Value);
          }

          header = header.HeaderNext;
        }
      }
    }

    public abstract void CreateImageDatabase(string path);
          
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

    public bool TryGetBlockBytes(byte[] hash, out byte[] buffer)
    {
      buffer = null;

      if (TryGetHeader(hash, out Header header))
        try
        {
          buffer = Archiver.LoadBlockBytes(header.Height);
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when loading block {hash.ToHexString()} from disk."
          .Log(this, LogEntryNotifier);
        }

      return buffer != null;
    }

    public virtual void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    { throw new NotImplementedException(); }

    public virtual void DeleteDB()
    { throw new NotImplementedException(); }

    public virtual void DeleteBlocksMinedUnconfirmed() { }

    public virtual void RevokeBlockInsertion()
    { throw new NotImplementedException(); }

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

      lock (HeaderIndex)
        if (HeaderIndex.TryGetValue(key, out List<Header> headers))
          header = headers.FirstOrDefault(h => headerHash.IsAllBytesEqual(h.Hash));

      return header != null;
    }

    void IndexingHeaderTip()
    {
      int keyHeader = BitConverter.ToInt32(HeaderTip.Hash, 0);

      lock (HeaderIndex)
      {
        if (!HeaderIndex.TryGetValue(keyHeader, out List<Header> headers))
        {
          headers = new List<Header>();
          HeaderIndex.Add(keyHeader, headers);
        }

        headers.Add(HeaderTip);
      }
    }

    void RemoveIndexHeaderTip()
    { }
  }
}
