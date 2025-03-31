using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    const int SIZE_BLOCK_MAX = 1 << 22; // 4 MB

    const int COUNT_BLOCKS_DOWNLOAD_DEPTH_MAX = 300;

    const long BLOCK_REWARD_INITIAL = 200000000000000; // 200 BTK
    const int PERIOD_HALVENING_BLOCK_REWARD = 105000;

    const long COUNT_SATOSHIS_PER_DAY_MINING = 500000;
    const long TIMESPAN_DAY_SECONDS = 24 * 3600;

    public DBAccounts DBAccounts;


    public enum TypesToken
    {
      Coinbase = 0,
      ValueTransfer = 1,
      AnchorToken = 2,
      Data = 3
    }


    public TokenBToken(ILogEntryNotifier logEntryNotifier, byte[] iDToken, UInt16 port, Token tokenParent)
      : base(
          port,
          iDToken,
          flagEnableInboundConnections: true,
          logEntryNotifier,
          tokenParent)
    {
      DBAccounts = new(GetName());

      Wallet = new WalletBToken(
        File.ReadAllText($"Wallet{GetName()}/wallet"), 
        this);

      TXPool = new PoolTXBToken(this);

      PathBlocksMined = Path.Combine(GetName(), "blocksMined");
      Directory.CreateDirectory(PathBlocksMined);

      SizeBlockMax = SIZE_BLOCK_MAX;

      BlockRewardInitial = BLOCK_REWARD_INITIAL;
      PeriodHalveningBlockReward = PERIOD_HALVENING_BLOCK_REWARD;
    }

    public override void LoadState()
    {
      Reset();

      LoadImageHeaderchain(); // Ich nehme an, dass die Headerchain mir sagt auf welcher height die DB ist.

      LoadBlocksFromArchive();

      TokensChild.ForEach(t => t.LoadState());
    }

    public override void ArchiveBlock(Block block)
    {
      WriteBlockToImageHeaderchain(block.Header);

      string pathFile = Path.Combine(GetName(), "blocks", block.Header.Height.ToString());

      using (FileStream fileStreamBlock = new(pathFile, FileMode.Create, FileAccess.Write, FileShare.None))
      {
        block.WriteToDisk(fileStreamBlock);
      }
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

          Wallet.InsertBlock(block);

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

    void LoadImageHeaderchain()
    {
      byte[] bytesHeaderImage = File.ReadAllBytes(Path.Combine(GetName(), "ImageHeaderchain"));

      int index = 0;

      $"Load headerchain of {GetName()}.".Log(this, LogFile, LogEntryNotifier);

      SHA256 sHA256 = SHA256.Create();

      while (index < bytesHeaderImage.Length)
      {
        Header header = ParseHeader(bytesHeaderImage, ref index, sHA256);

        header.CountBytesTXs = BitConverter.ToInt32(bytesHeaderImage, index);
        index += 4;

        int countHashesChild = VarInt.GetInt(bytesHeaderImage, ref index);

        for (int i = 0; i < countHashesChild; i++)
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

    void WriteBlockToImageHeaderchain(Header header)
    {
      using (FileStream fileImageHeaderchain = new(
          Path.Combine(GetName(), "ImageHeaderchain"),
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
      {
        byte[] headerBytes = header.Serialize();

        fileImageHeaderchain.Write(headerBytes);

        fileImageHeaderchain.Write(BitConverter.GetBytes(header.CountBytesTXs));

        fileImageHeaderchain.Write(VarInt.GetBytes(header.HashesChild.Count));

        foreach (var hashChild in header.HashesChild)
        {
          fileImageHeaderchain.Write(hashChild.Key);
          fileImageHeaderchain.Write(hashChild.Value);
        }
      }
    }

    public override Header CreateHeaderGenesis()
    {
      HeaderBToken header = new(
        headerHash: "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f".ToBinary(),
        hashPrevious: "0000000000000000000000000000000000000000000000000000000000000000".ToBinary(),
        merkleRootHash: "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b".ToBinary(),
        hashDatabase: "0000000000000000000000000000000000000000000000000000000000000000".ToBinary(),
        unixTimeSeconds: 1231006505,
        nonce: 0);

      return header;
    }

    public override TX ParseTXCoinbase(byte[] buffer, ref int startIndex, SHA256 sHA256, long blockReward)
    {
      TypesToken typeToken = (TypesToken)buffer[startIndex];
      startIndex += 1;

      if (typeToken != TypesToken.Coinbase)
        throw new ProtocolException($"First tX of block is not of type coinbase.");

      return new TXBTokenCoinbase(buffer, ref startIndex, sHA256);
    }

    public override TX ParseTX(byte[] buffer, ref int startIndex, SHA256 sHA256)
    {
      TypesToken typeToken = (TypesToken)buffer[startIndex];
      startIndex += 1;

      if (typeToken == TypesToken.ValueTransfer)
        return new TXBTokenValueTransfer(buffer, ref startIndex, sHA256);
      
      if (typeToken == TypesToken.AnchorToken)
        return new TXBTokenAnchor(buffer, ref startIndex, sHA256);
      
      if (typeToken == TypesToken.Data)
        return new TXBTokenData(buffer, ref startIndex, sHA256);

      throw new ProtocolException($"Unknown / wrong token type {typeToken}.");
    }

    public override void InsertBlockInDB(Block block)
    {
      try
      {
        foreach (TXBToken tX in block.TXs)
          if (tX is TXBTokenCoinbase tXCoinbase)
          {
            foreach (TXOutputBToken tXOutput in tXCoinbase.TXOutputs)
              DBAccounts.InsertOutput(tXOutput, block.Header.Height);
          }
          else
          {
            DBAccounts.SpendInput(tX);

            if (tX is TXBTokenValueTransfer tXTokenTransfer)
            {
              foreach (TXOutputBToken tXOutput in tXTokenTransfer.TXOutputs)
                DBAccounts.InsertOutput(tXOutput, block.Header.Height);
            }
            else if (tX is TXBTokenAnchor tXBTokenAnchor)
            { }
            else if (tX is TXBTokenData tXBTokenData)
            { }
            else throw new ProtocolException($"Type of transaction {tX} is not supported by protocol.");
          }
      }
      catch (Exception ex)
      {
        DBAccounts.PurgeStagedData();

        throw ex;
      }

      DBAccounts.Commit();
    }

    public override void ReverseBlockInDB(Block block)
    {
      for (int i = block.TXs.Count - 1; i >= 0; i--)
      {
        TXBToken tX = block.TXs[i] as TXBToken;

        if (tX is TXBTokenCoinbase tXCoinbase)
        {
          foreach (TXOutputBToken tXOutput in tXCoinbase.TXOutputs)
            DBAccounts.ReverseOutput(tXOutput);
        }
        else
        {
          DBAccounts.ReverseSpendInput(tX);

          if (tX is TXBTokenValueTransfer tXTokenTransfer)
          {
            foreach (TXOutputBToken tXOutput in tXTokenTransfer.TXOutputs)
              DBAccounts.ReverseOutput(tXOutput);
          }
          else if (tX is TXBTokenAnchor tXBTokenAnchor)
          { }
          else if (tX is TXBTokenData tXBTokenData)
          { }
          else throw new ProtocolException($"Type of transaction {tX} is not supported by protocol.");
        }
      }
    }

    public override void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    {
      DBAccounts.InsertDB(bufferDB, lengthDataInBuffer);
    }

    public override void DeleteDB()
    { 
      DBAccounts.Delete(); 
    }

    public override List<byte[]> ParseHashesDB(byte[] buffer, int length, Header headerTip)
    {
      SHA256 sHA256 = SHA256.Create();

      byte[] hashRootHashesDB = sHA256.ComputeHash(buffer, 0, length);

      if (!((HeaderBToken)headerTip).HashDatabase.IsAllBytesEqual(hashRootHashesDB))
        throw new ProtocolException($"Root hash of hashesDB not equal to database hash in header tip");

      List<byte[]> hashesDB = new();

      for (int i = 0; i < DBAccounts.COUNT_CACHES + DBAccounts.COUNT_FILES_DB; i += 32)
      {
        byte[] hashDB = new byte[32];
        Array.Copy(buffer, i, hashDB, 0, 32);
        hashesDB.Add(hashDB);
      }

      return hashesDB;
    }
      
    byte[] GetGenesisBlockBytes()
    {
      return new byte[285]{
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x3b, 0xa3, 0xed, 0xfd, 0x7a, 0x7b, 0x12, 0xb2, 0x7a, 0xc7, 0x2c, 0x3e,
        0x67, 0x76, 0x8f, 0x61, 0x7f, 0xc8, 0x1b, 0xc3, 0x88, 0x8a, 0x51, 0x32, 0x3a, 0x9f, 0xb8, 0xaa,
        0x4b, 0x1e, 0x5e, 0x4a, 0x29, 0xab, 0x5f, 0x49, 0xff, 0xff, 0x00, 0x1d, 0x1d, 0xac, 0x2b, 0x7c,
        0x01, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0x4d, 0x04, 0xff, 0xff, 0x00, 0x1d,
        0x01, 0x04, 0x45, 0x54, 0x68, 0x65, 0x20, 0x54, 0x69, 0x6d, 0x65, 0x73, 0x20, 0x30, 0x33, 0x2f,
        0x4a, 0x61, 0x6e, 0x2f, 0x32, 0x30, 0x30, 0x39, 0x20, 0x43, 0x68, 0x61, 0x6e, 0x63, 0x65, 0x6c,
        0x6c, 0x6f, 0x72, 0x20, 0x6f, 0x6e, 0x20, 0x62, 0x72, 0x69, 0x6e, 0x6b, 0x20, 0x6f, 0x66, 0x20,
        0x73, 0x65, 0x63, 0x6f, 0x6e, 0x64, 0x20, 0x62, 0x61, 0x69, 0x6c, 0x6f, 0x75, 0x74, 0x20, 0x66,
        0x6f, 0x72, 0x20, 0x62, 0x61, 0x6e, 0x6b, 0x73, 0xff, 0xff, 0xff, 0xff, 0x01, 0x00, 0xf2, 0x05,
        0x2a, 0x01, 0x00, 0x00, 0x00, 0x43, 0x41, 0x04, 0x67, 0x8a, 0xfd, 0xb0, 0xfe, 0x55, 0x48, 0x27,
        0x19, 0x67, 0xf1, 0xa6, 0x71, 0x30, 0xb7, 0x10, 0x5c, 0xd6, 0xa8, 0x28, 0xe0, 0x39, 0x09, 0xa6,
        0x79, 0x62, 0xe0, 0xea, 0x1f, 0x61, 0xde, 0xb6, 0x49, 0xf6, 0xbc, 0x3f, 0x4c, 0xef, 0x38, 0xc4,
        0xf3, 0x55, 0x04, 0xe5, 0x1e, 0xc1 ,0x12, 0xde, 0x5c, 0x38, 0x4d, 0xf7, 0xba, 0x0b, 0x8d, 0x57,
        0x8a, 0x4c, 0x70, 0x2b, 0x6b, 0xf1, 0x1d, 0x5f, 0xac, 0x00, 0x00 ,0x00 ,0x00 };
    }

    public override void Reset()
    {
      base.Reset();
      DBAccounts.ClearCache();
    }

    public override bool TryGetDB(byte[] hash, out byte[] dataDB)
    {
      return DBAccounts.TryGetDB(hash, out dataDB);
    }

    public override List<string> GetSeedAddresses()
    {
      return new List<string>()
      {
        "83.229.86.158" 
        //84.74.69.100
      };
    }

    public Account GetAccountUnconfirmed(byte[] iDAccount)
    {
      if(!DBAccounts.TryGetAccount(iDAccount, out Account account))
        throw new ProtocolException($"Account {iDAccount} not in database.");

      return ((PoolTXBToken)TXPool).ApplyTXsOnAccount(account);
    }

    public override HeaderBToken ParseHeader(byte[] buffer, ref int index, SHA256 sHA256)
    {
      byte[] hash =
        sHA256.ComputeHash(
          sHA256.ComputeHash(
            buffer,
            index,
            HeaderBToken.COUNT_HEADER_BYTES));

      byte[] hashHeaderPrevious = new byte[32];
      Array.Copy(buffer, index, hashHeaderPrevious, 0, 32);
      index += 32;

      byte[] merkleRootHash = new byte[32];
      Array.Copy(buffer, index, merkleRootHash, 0, 32);
      index += 32;

      byte[] hashDatabase = new byte[32];
      Array.Copy(buffer, index, hashDatabase, 0, 32);
      index += 32;

      uint unixTimeSeconds = BitConverter.ToUInt32(
        buffer, index);
      index += 4;

      uint nonce = BitConverter.ToUInt32(buffer, index);
      index += 4;

      return new HeaderBToken(
        hash,
        hashHeaderPrevious,
        merkleRootHash,
        hashDatabase,
        unixTimeSeconds,
        nonce);
    }
  }
}
