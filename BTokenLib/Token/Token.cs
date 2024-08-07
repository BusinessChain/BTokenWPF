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
    public ILogEntryNotifier LogEntryNotifier;

    public Token TokenParent;
    public List<Token> TokensChild = new();

    public Header HeaderGenesis;
    public Header HeaderTip;

    Dictionary<int, List<Header>> HeaderIndex = new();

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

    public byte[] IDToken;

    bool IsLocked;
    static object LOCK_Token = new();


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

      PathImage = Path.Combine(PathRootToken, NameImage);
      PathImageOld = Path.Combine(PathRootToken, NameImageOld);

      HeaderGenesis = CreateHeaderGenesis();
      HeaderTip = HeaderGenesis;

      IndexingHeaderTip();

      Archiver = new(this);

      Network = new(this, port, flagEnableInboundConnections);
    }

    public void Start()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = TokenParent;

      token.LoadImage();
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

        while (index < bytesHeaderImage.Length)
        {
          Header header = ParseHeader(
            bytesHeaderImage,
            ref index);

          heightHeader += 1;

          header.CountBytesTXs = BitConverter.ToInt32(
            bytesHeaderImage, index);

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

    public bool TryStartSynchronization()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = token.TokenParent;

      return token.Network.TryStartSynchronization();
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

    public void ForkChain(int heightFork)
    {
      LoadImage(heightFork);
      Archiver.SetBlockPathToFork();
    }

    public void Reorganize()
    {
      Archiver.Reorganize();
    }

    public void LoadImage(int heightMax = int.MaxValue)
    {
      Reset();

      LoadPool(); 

      string pathImage = Path.Combine(GetName(), NameImage);

      while (true)
        try
        {
          ($"Load image of token {pathImage}" +
            $"{(heightMax < int.MaxValue ? $" with maximal height {heightMax}" : "")}.")
            .Log(this, LogFile, LogEntryNotifier);

          LoadImageHeaderchain(pathImage);
          LoadImageDatabase(pathImage);
          Wallet.LoadImage(pathImage);

          if (HeaderTip.Height > heightMax)
            throw new ProtocolException(
              $"Image height of {GetName()} higher than desired height {heightMax}.");

          break;
        }
        catch
        {
          Reset();

          if (Directory.Exists(pathImage))
            Directory.Delete(pathImage, recursive: true);

          try
          {
            Directory.Move(
              Path.Combine(GetName(), NameImageOld),
              pathImage);
          }
          catch (DirectoryNotFoundException)
          {
            break;
          }
        }

      int heightBlock = HeaderTip.Height + 1;

      try
      {
        while (
          heightBlock <= heightMax &&
          Archiver.TryLoadBlock(heightBlock, out Block block))
        {
          block.Header.AppendToHeader(HeaderTip);

          InsertInDatabase(block);

          HeaderTip.HeaderNext = block.Header;
          HeaderTip = block.Header;

          IndexingHeaderTip();

          heightBlock += 1;
        }
      }
      finally
      {
        $"{this} completed loading from disk with block tip {HeaderTip} at height {HeaderTip.Height}."
          .Log(this, LogFile, LogEntryNotifier);
      }

      TokensChild.ForEach(t => t.LoadImage(HeaderTip.Height));
    }

    public abstract void LoadPool();

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

      while (index < bytesHeaderImage.Length)
      {
        Header header = ParseHeader(
          bytesHeaderImage,
          ref index);

        header.CountBytesTXs = BitConverter.ToInt32(bytesHeaderImage, index);
        index += 4;

        int countHashesChild = VarInt.GetInt(bytesHeaderImage, ref index);
        
        for(int i = 0; i < countHashesChild; i += 1)
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

    public void InsertBlock(Block block)
    {
      $"Insert block {block} in {this}.".Log(this, LogEntryNotifier);

      block.Header.AppendToHeader(HeaderTip);

      InsertInDatabase(block);

      HeaderTip.HeaderNext = block.Header;
      HeaderTip = block.Header;

      IndexingHeaderTip();

      Archiver.ArchiveBlock(block);

      if (block.Header.Height % INTERVAL_BLOCKHEIGHT_IMAGE == 0)
        CreateImage();

      TokensChild.ForEach(t => t.SignalParentBlockInsertion(block.Header));
    }

    protected abstract void StageTXInDatabase(TX tX, Header header);
    protected abstract void CommitTXsInDatabase();
    protected abstract void DiscardStagedTXsInDatabase();

    protected void InsertInDatabase(Block block)
    {
      byte[] targetValue = SHA256.HashData(block.Header.Hash);

      Dictionary<byte[], byte[]> biggestDifferencesTemp =
        new(new EqualityComparerByteArray());
      byte[] biggestDifferenceTemp;

      Dictionary<byte[], TX> tXAnchorWinners =
        new(new EqualityComparerByteArray());
      TX tXAnchorWinner;

      try
      {
        foreach (TX tX in block.TXs)
        {
          StageTXInDatabase(tX, block.Header);

          if (tX.TryGetAnchorToken(out TokenAnchor tokenAnchor))
          {
            $"Detected anchor token {tX} in block {block} referencing {tokenAnchor.HashBlockReferenced.ToHexString()}.".Log(this, LogEntryNotifier);

            if (!biggestDifferencesTemp.TryGetValue(tokenAnchor.IDToken, out biggestDifferenceTemp))
              biggestDifferenceTemp = new byte[32];

            tXAnchorWinners.TryGetValue(tokenAnchor.IDToken, out tXAnchorWinner);

            byte[] differenceHash = targetValue.SubtractByteWise(tX.Hash);

            if (differenceHash.IsGreaterThan(biggestDifferenceTemp))
            {
              biggestDifferencesTemp[tokenAnchor.IDToken] = differenceHash;
              tXAnchorWinners[tokenAnchor.IDToken] = tX;
              block.Header.HashesChild[tokenAnchor.IDToken] = tokenAnchor.HashBlockReferenced;
            }
            else if(tX.IsSuccessorTo(tXAnchorWinner))
            {
              tXAnchorWinners[tokenAnchor.IDToken] = tX;
              block.Header.HashesChild[tokenAnchor.IDToken] = tokenAnchor.HashBlockReferenced;
            }

            TokensChild.FirstOrDefault(t => t.IDToken.IsAllBytesEqual(tokenAnchor.IDToken))
              ?.ReceiveAnchorTokenConfirmed(tokenAnchor);
          }
        }
      }
      catch(Exception ex)
      {
        DiscardStagedTXsInDatabase();

        throw ex;
      }

      CommitTXsInDatabase();
    }

    public abstract void AddTXToPool(TX tX);

    public abstract bool TryGetFromTXPool(byte[] hashTX, out TX tX);

    public abstract List<TX> GetTXsFromPool();

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

    public abstract Block CreateBlock();

    public Block ParseBlock(Stream stream)
    {
      Block block = CreateBlock();

      block.Header = ParseHeader(stream);

      block.ParseTXs(stream);

      return block;
    }

    public abstract Header ParseHeader(byte[] buffer, ref int index);
    public abstract Header ParseHeader(Stream stream);

    public abstract TX ParseTX(Stream stream, SHA256 sHA256);

    public bool TrySendTX(string address, long value, double feePerByte, out TX tX)
    {
      if (Wallet.TryCreateTX(address, value, feePerByte, out tX))
      {
        BroadcastTX(tX);
        Wallet.AddTXUnconfirmedToHistory(tX);
        return true;
      }

      return false;
    }

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
      if (TryGetHeader(hash, out Header header))
        if (Archiver.TryLoadBlockBytes(header.Height, out buffer))
          return true;

      buffer = null;
      return false;
    }

    public virtual void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    { throw new NotImplementedException(); }

    public virtual void DeleteDB()
    { throw new NotImplementedException(); }

    public virtual void ReceiveAnchorTokenConfirmed(TokenAnchor tokenAnchor)
    { throw new NotImplementedException(); }

    public virtual void SignalParentBlockInsertion(Header header)
    { throw new NotImplementedException(); }

    public virtual void CacheAnchorTokenUnconfirmedIfSelfMined(TokenAnchor tokenAnchor)
    { throw new NotImplementedException(); }

    public virtual void RevokeBlockInsertion()
    { throw new NotImplementedException(); }

    public virtual List<byte[]> ParseHashesDB(byte[] buffer, int length, Header headerTip)
    { throw new NotImplementedException(); }

    public string GetName()
    {
      return GetType().Name;
    }

    public virtual bool TryGetDB(
      byte[] hash,
      out byte[] dataDB)
    { throw new NotImplementedException(); }

    void BroadcastTX(TX tX)
    {
      AddTXToPool(tX);

      SendAnchorTokenUnconfirmedToChilds(tX);

      Network.AdvertizeTX(tX);
    }

    public void SendAnchorTokenUnconfirmedToChilds(TX tX)
    {
      if (tX.TryGetAnchorToken(out TokenAnchor tokenAnchor))
        TokensChild.FirstOrDefault(t => t.IDToken.IsAllBytesEqual(tokenAnchor.IDToken))
          ?.CacheAnchorTokenUnconfirmedIfSelfMined(tokenAnchor);
    }

    public bool TryRBFAnchorToken(
      TokenAnchor tokenAnchorOld,
      TokenAnchor tokenAnchorNew)
    {
      Wallet.ReverseTX(tokenAnchorOld.TX);

      return TryBroadcastAnchorToken(tokenAnchorNew);
    }

    public bool TryBroadcastAnchorToken(TokenAnchor tokenAnchor)
    {
      byte[] dataAnchorToken = tokenAnchor.Serialize();

      if (Wallet.TryCreateTXData(
        dataAnchorToken, 
        tokenAnchor.NumberSequence,
        tokenAnchor.FeeSatoshiPerByte,
        out TX tX))
      {
        tokenAnchor.TX = tX;

        BroadcastTX(tX);

        Wallet.AddTXUnconfirmedToHistory(tX);

        $"Created and broadcasted anchor token {tokenAnchor} referencing {tokenAnchor.HashBlockReferenced.ToHexString()}."
          .Log(this, LogEntryNotifier);

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

    public List<Header> GetHeaders(
      IEnumerable<byte[]> locatorHashes,
      int count,
      byte[] stopHash)
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
      int key = BitConverter.ToInt32(headerHash, 0);

      lock (HeaderIndex)
        if (HeaderIndex.TryGetValue(
          key,
          out List<Header> headers))
        {
          foreach (Header h in headers)
            if (headerHash.IsAllBytesEqual(h.Hash))
            {
              header = h;
              return true;
            }
        }

      header = null;
      return false;
    }

    public void IndexingHeaderTip()
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
  }
}
