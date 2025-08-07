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

    DBAccounts DBAccounts;


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
      PathFileHeaderchain = Path.Combine(GetName(), "ImageHeaderchain");

      Wallet = new WalletBToken(File.ReadAllText($"Wallet{GetName()}/wallet"), this);

      TXPool = new PoolTXBToken(this);

      PathBlocksMined = Path.Combine(GetName(), "blocksMined");
      Directory.CreateDirectory(PathBlocksMined);

      SizeBlockMax = SIZE_BLOCK_MAX;

      BlockRewardInitial = BLOCK_REWARD_INITIAL;
      PeriodHalveningBlockReward = PERIOD_HALVENING_BLOCK_REWARD;

      for (int i = 0; i < COUNT_FILES_DB; i++)
        FilesDB.Add(new FileDB(Path.Combine(PathRootDB, i.ToString())));

    }

    public override void LoadImageHeaderchain()
    {
      SHA256 sHA256 = SHA256.Create();

      int indexHeaderFile = 0;
      List<Header> headerchainStrongest = null;
      int indexHeaderFileStrongest = 0;

      const int header_File_Count = 2;

      while (indexHeaderFile < header_File_Count)
      {
        string pathFileHeaderchain = PathFileHeaderchain + indexHeaderFile.ToString();

        if (!File.Exists(pathFileHeaderchain))
          continue;

        $"Load headerchain file {pathFileHeaderchain}.".Log(this, LogFile, LogEntryNotifier);

        byte[] bytesHeaderImage = File.ReadAllBytes(pathFileHeaderchain);

        List<Header> headerchain = new();
        Header headerTip = HeaderTip;
        int startIndex = 0;

        while (startIndex < bytesHeaderImage.Length)
          try
          {
            Header header = ParseHeader(bytesHeaderImage, ref startIndex, sHA256);

            header.CountBytesTXs = BitConverter.ToInt32(bytesHeaderImage, startIndex);
            startIndex += 4;

            int countHashesChild = VarInt.GetInt(bytesHeaderImage, ref startIndex);

            for (int i = 0; i < countHashesChild; i++)
            {
              byte[] iDToken = new byte[IDToken.Length];
              Array.Copy(bytesHeaderImage, startIndex, iDToken, 0, iDToken.Length);
              startIndex += iDToken.Length;

              byte[] hashesChild = new byte[32];
              Array.Copy(bytesHeaderImage, startIndex, hashesChild, 0, 32);
              startIndex += 32;

              header.HashesChild.Add(iDToken, hashesChild);
            }

            header.AppendToHeader(headerTip);
            headerTip = header;

            headerchain.Add(headerTip);
          }
          catch (Exception ex)
          {
            $"Failed to parse header at index {startIndex}: {ex.Message}".Log(this, LogFile, LogEntryNotifier);
            break;
          }

        if (headerchain.Any())
          if (headerchainStrongest == null || headerchain.Last().DifficultyAccumulated > headerchainStrongest.Last().DifficultyAccumulated)
          {
            headerchainStrongest = headerchain;
            indexHeaderFileStrongest = indexHeaderFile;
          }

        indexHeaderFile += 1;
      }

      if (headerchainStrongest != null)
      {
        headerchainStrongest.ForEach(header =>
        {
          HeaderTip.HeaderNext = header;
          HeaderTip = header;

          IndexingHeaderTip();
        });

        HeightBlockchainDatabaseOnDisk = HeaderTip.Height;

        string pathFileHeaderchainStronger = PathFileHeaderchain + indexHeaderFileStrongest.ToString();
        string pathFileHeaderchainWeaker;

        if (indexHeaderFileStrongest == 0)
          pathFileHeaderchainWeaker = PathFileHeaderchain + 1.ToString();
        else
          pathFileHeaderchainWeaker = PathFileHeaderchain + 0.ToString();

        File.Copy(pathFileHeaderchainStronger, pathFileHeaderchainWeaker);
      }
    }

    public override void LoadBlocksFromArchive()
    {
      int heightBlock = HeaderTip.Height + 1;

      while (TryLoadBlock(heightBlock, out Block block))
        try
        {
          $"Load block {block}.".Log(this, LogEntryNotifier);

          block.Header.AppendToHeader(HeaderTip);

          InsertBlockInDatabase(block);

          Wallet.InsertBlock(block);

          heightBlock += 1;
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when inserting block {block}, height {heightBlock} loaded from disk: \n{ex.Message}. \nBlock is deleted."
          .Log(this, LogEntryNotifier);

          File.Delete(Path.Combine(PathBlockArchive, heightBlock.ToString()));
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

    // Evt. DB Accounts nach hier transferieren
    Dictionary<byte[], Account> Cache = new();
    Dictionary<byte[], Account> AccountsStaged;
    const int COUNT_MAX_ACCOUNTS_IN_CACHE = 40000; // Read from configuration file
    const double COUNT_MAX_ACCOUNTS_IN_CACHE_HYSTERESIS = 0.9;
    int HeightBlockchainDatabaseOnDisk;

    string PathRootDB;
    public const int COUNT_FILES_DB = 256;
    List<FileDB> FilesDB = new();
    byte[] HashesFilesDB = new byte[COUNT_FILES_DB * 32];

    public override void InsertBlockInDatabase(Block block)
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

      string pathFileBlock = Path.Combine(GetName(), PathBlockArchive, block.Header.Height.ToString());
      using (FileStream fileStreamBlock = new(pathFileBlock, FileMode.Create, FileAccess.Write))
        block.WriteToDisk(fileStreamBlock);

      Commit();
    }

    public void Commit()
    {
      foreach (var account in AccountsStaged)
        if (account.Value.Balance == 0)
          Cache.Remove(account.Key);
        else
          Cache[account.Key] = account.Value;

      AccountsStaged.Clear();

      if (Cache.Count > COUNT_MAX_ACCOUNTS_IN_CACHE)
      {
        int heightBlock = HeightBlockchainDatabaseOnDisk + 1;

        while (Cache.Count > COUNT_MAX_ACCOUNTS_IN_CACHE * COUNT_MAX_ACCOUNTS_IN_CACHE_HYSTERESIS)
          if (TryLoadBlock(heightBlock, out Block block))
          {
            try
            {
              $"Load block {block} for insertion in disk database and removal from cache.".Log(this, LogEntryNotifier);

              TXBTokenCoinbase tXBTokenCoinbase = block.TXs[0] as TXBTokenCoinbase;


              foreach (TXBToken tX in block.TXs.Skip(1))
                if (TrySpendInputOnDisk(tX))
                  InsertOutputsInDatabaseOnDisk(tX, block.Header.Height);

              block.Header.WriteToDiskAtomic(PathFileHeaderchain + 0.ToString());
              block.Header.WriteToDiskAtomic(PathFileHeaderchain + 1.ToString());

              RemoveBlockFromCache(block);

              heightBlock += 1;
            }
            catch (ProtocolException ex)
            {
              $"{ex.GetType().Name} when inserting block {block}, height {heightBlock} loaded from disk: \n{ex.Message}. \nBlock is deleted."
              .Log(this, LogEntryNotifier);

              File.Delete(Path.Combine(PathBlockArchive, heightBlock.ToString()));
            }
          }
          else
          {
            // Reload state
          }

        // Load headerFile for writing after the dump.
        // Dump the block after block into the database (blocks can be assumed validated)
        // After each block database dump, update headerchain file and remove block from cache
        // If cache size is small enough (400MB plus hysteresis) then exit dumping.
        // Delete unused blocks that were dumped the last time and delete zero accounts that were zero the last time
      }
    }

    public bool TrySpendInputOnDisk(TXBToken tX)
    {
      Account account;

      if (tX is TXBTokenCoinbase)
        if (!FilesDB[tX.IDAccountSource[0]].TryGetAccount(tX.IDAccountSource, out account))
        {
          account = new() { ID = tX.IDAccountSource };
          // store account on disk, flush
          return true;
        }
        else if (account.BlockHeightAccountInit == tX.BlockheightAccountInit)
          return false; // Assume this transaction has already been inserted into the database at an earlier attempt.

      if (!FilesDB[tX.IDAccountSource[0]].TryGetAccount(tX.IDAccountSource, out account))
        throw new ProtocolException($"Account {tX.IDAccountSource.ToHexString()} referenced by TX {tX} not found in database.");
      // kann man das irgendwie fixen, konnte das durch ein frühere insertierung hervorgerufen werden?

      accountStaged.SpendTX(tX);
      AccountsStaged.Add(tX.IDAccountSource, accountStaged);
    }

    public void InsertOutputsInDatabaseOnDisk(TXBToken tX, int blockHeight)
    {
      foreach (TXOutputBToken output in tX.TXOutputs)
        if (FilesDB[output.IDAccount[0]].TryGetAccount(output.IDAccount, out Account account))
        {
          account.Balance += output.Value;
          FilesDB.UpdateAccount(account);
        }
        else
        {
          account = new()
          {
            ID = output.IDAccount,
            BlockHeightAccountInit = blockHeight,
            Balance = output.Value
          };

          FilesDB.InsertAccount(account);
        }
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

      block.Header.ReverseHeaderOnDiskAtomic(PathFileHeaderchain);
    }

    public override void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    {
      int startIndex = 0;

      FileDB fileDB = new(Path.Combine(PathRootDB, bufferDB[startIndex].ToString()));
      fileDB.Write(bufferDB, startIndex, lengthDataInBuffer - 1);

      FilesDB.Add(fileDB);
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
      for (int i = 0; i < HashesFilesDB.Length; i++)
        if (hash.IsAllBytesEqual(HashesFilesDB, i * 32))
        {
          dataDB = new byte[FilesDB[i].Length];
          FilesDB[i].Write(dataDB, 0, dataDB.Length);
          return true;
        }

      dataDB = null;
      return false;
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
