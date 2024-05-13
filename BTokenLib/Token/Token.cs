﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;
using System.Linq;

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

    protected BlockArchiver Archiver;

    public Wallet Wallet;

    public Network Network;

    public StreamWriter LogFile;

    const string NameImage = "Image";
    const string NameImageOld = "ImageOld";

    string PathImage;
    string PathImageOld;
    string PathRootToken;

    const int INTERVAL_BLOCKHEIGHT_IMAGE = 50;
    const int ORDER_AVERAGEING_FEEPERBYTE = 3;
    double FeeSatoshiPerBytePriorityHigh = 10;

    public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' };
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
        false);

      PathImage = Path.Combine(PathRootToken, NameImage);
      PathImageOld = Path.Combine(PathRootToken, NameImageOld);

      HeaderGenesis = CreateHeaderGenesis();
      HeaderTip = HeaderGenesis;

      IndexingHeaderTip();

      Archiver = new(GetName());

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
      new Thread(Network.Start).Start();

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
          Block block = CreateBlock();

          Header header = block.ParseHeader(
            bytesHeaderImage,
            ref index);

          heightHeader += 1;

          header.CountBytesBlock = BitConverter.ToInt32(
            bytesHeaderImage, index);

          index += 4;

          text += $"{heightHeader}, {header}";

          byte flagHasHashChild = bytesHeaderImage[index];
          index += 1;

          if (flagHasHashChild == 0x01)
          {
            header.HashChild = new byte[32];
            Array.Copy(bytesHeaderImage, index, header.HashChild, 0, 32);
            index += 32;

            text += $" -> {header.HashChild.ToHexString()}";
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

      while (TryGetBlock(i, out Block block))
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

      while (
        heightBlock <= heightMax &&
        TryGetBlock(heightBlock, out Block block))
      {
        $"Pull block height {heightBlock} from Archiver of {GetName()}."
          .Log(this, LogFile, LogEntryNotifier);

        try
        {
          InsertBlock(block);
        }
        catch
        {
          Archiver.CleanAfterBlockHeight(HeaderTip.Height);
          break;
        }

        heightBlock += 1;
      }

      TokensChild.ForEach(t => t.LoadImage(HeaderTip.Height));
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

      Block block = CreateBlock();

      int index = 0;

      $"Load headerchain of {GetName()}.".Log(this, LogFile, LogEntryNotifier);

      while (index < bytesHeaderImage.Length)
      {
        Header header = block.ParseHeader(
          bytesHeaderImage,
          ref index);

        header.CountBytesBlock = BitConverter.ToInt32(
          bytesHeaderImage, index);

        index += 4;

        byte flagHasHashChild = bytesHeaderImage[index];
        index += 1;

        if(flagHasHashChild == 0x01)
        {
          header.HashChild = new byte[32];
          Array.Copy(bytesHeaderImage, index, header.HashChild, 0, 32);
          index += 32;
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
      $"Insert block {block}.".Log(this, LogFile, LogEntryNotifier);

      block.Header.AppendToHeader(HeaderTip);
      InsertInDatabase(block);
      AppendHeaderToTip(block.Header);

      //FeeSatoshiPerBytePriorityHigh =
      //  ((ORDER_AVERAGEING_FEEPERBYTE - 1) * FeeSatoshiPerBytePriorityHigh + block.FeePerByte) /
      //  ORDER_AVERAGEING_FEEPERBYTE;

      TokensChild.ForEach(t => t.SignalParentBlockInsertion(block.Header));

      Archiver.ArchiveBlock(block);

      if (block.Header.Height % INTERVAL_BLOCKHEIGHT_IMAGE == 0)
        CreateImage();
    }

    public abstract void RBFAnchorTokens(
      ref List<TokenAnchor> tokensAnchorRBF,
      TokenAnchor tokenAnchorNew);

    protected abstract void InsertInDatabase(Block block);

    public abstract bool TryAddTXPool(TX tX);

    public abstract bool TryGetFromTXPool(byte[] hashTX, out TX tX);

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

          fileImageHeaderchain.Write(
            headerBytes, 0, headerBytes.Length);

          fileImageHeaderchain.Write(
            BitConverter.GetBytes(header.CountBytesBlock), 0, 4);

          if(header.HashChild == null)
            fileImageHeaderchain.WriteByte(0x00);
          else
          {
            fileImageHeaderchain.WriteByte(0x01);
            fileImageHeaderchain.Write(header.HashChild, 0, 32);
          }

          header = header.HeaderNext;
        }
      }
    }

    public abstract void CreateImageDatabase(string path);

    public abstract Block CreateBlock();

    public abstract TX ParseTX(
      Stream stream,
      SHA256 sHA256,
      bool flagCoinbase);

    public bool MakeTX(string address, long value, double feePerByte, out TX tX)
    {
      //if (this is TokenBitcoin)
      //  address = "184Z6YZ6qt6wbpyuKXKsyDTeXX5yNuYwWk";
      //else if (this is TokenBToken)
      //  address = "1MaJjBpbqYSa1ui3cNPGwMvVqzg2mwKp9P";

      return Wallet.TryCreateTX(
        address,
        value,
        feePerByte,
        out tX);
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
        if (Archiver.TryLoadBlock(header.Height, out buffer))
          return true;

      buffer = null;
      return false;
    }

    public bool TryGetBlock(int heightHeader, out Block block)
    {
      block = CreateBlock();

      if (Archiver.TryLoadBlock(heightHeader, block))
        return true;

      block = null;
      return false;
    }

    public virtual void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    { throw new NotImplementedException(); }

    public virtual void DeleteDB()
    { throw new NotImplementedException(); }

    public virtual void SignalAnchorTokenDetected(TokenAnchor tokenAnchor)
    { throw new NotImplementedException(); }

    public virtual void SignalParentBlockInsertion(Header header)
    { throw new NotImplementedException(); }

    public virtual void RevokeBlockInsertion()
    { throw new NotImplementedException(); }

    public virtual List<byte[]> ParseHashesDB(
      byte[] buffer,
      int length,
      Header headerTip)
    { throw new NotImplementedException(); }

    public string GetName()
    {
      return GetType().Name;
    }

    public virtual bool TryGetDB(
      byte[] hash,
      out byte[] dataDB)
    { throw new NotImplementedException(); }

    public void BroadcastTX(TX tX)
    {
      TryAddTXPool(tX);
      Network.AdvertizeTX(tX);
    }

    public bool TryBroadcastAnchorToken(TokenAnchor tokenAnchor)
    {
      byte[] dataAnchorToken = IDENTIFIER_BTOKEN_PROTOCOL
      .Concat(tokenAnchor.IDToken)
      .Concat(tokenAnchor.HashBlockReferenced)
      .Concat(tokenAnchor.HashBlockPreviousReferenced).ToArray();

      if (Wallet.TryCreateTXData(
        dataAnchorToken, 
        tokenAnchor.NumberSequence, 
        FeeSatoshiPerBytePriorityHigh,
        out TX tX))
      {
        tokenAnchor.TX = tX;
        BroadcastTX(tX);
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
            !header.Hash.IsEqual(stopHash))
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

    public void AppendHeaderToTip(Header header)
    {
      HeaderTip.HeaderNext = header;
      HeaderTip = header;

      IndexingHeaderTip();
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
            if (headerHash.IsEqual(h.Hash))
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
