using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class DatabaseAccounts
  {
    public const int COUNT_CACHES = 256;
    byte[] HashesCaches = new byte[COUNT_CACHES * 32];
    const int COUNT_MAX_CACHE = 40000; // Read from configuration file
    List<CacheDatabaseAccounts> Caches = new();
    int IndexCache;

    const string PathRootDB = "FilesDB";
    public const int COUNT_FILES_DB = 256; // file index will be idAccount[0]
    List<FileDB> FilesDB = new();
    byte[] HashesFilesDB = new byte[COUNT_FILES_DB * 32];

    const int LENGTH_RECORD_DB = 41;
    const int LENGTH_ID_ACCOUNT = 25;

    SHA256 SHA256 = SHA256.Create();
    byte[] Hash;

    public DatabaseAccounts()
    {
      for (int i = 0; i < COUNT_CACHES; i += 1)
        Caches.Add(new CacheDatabaseAccounts());

      Directory.CreateDirectory(
        Path.Combine(Directory.GetCurrentDirectory(), PathRootDB));

      for (int i = 0; i < COUNT_FILES_DB; i += 1)
        FilesDB.Add(new FileDB(
          Path.Combine(PathRootDB, i.ToString())));
    }

    public void LoadImage(string path)
    {
      byte[] bytesRecord = new byte[LENGTH_RECORD_DB];

      for (int i = 0; i < COUNT_CACHES; i += 1)
        using (FileStream fileCache = new(
          Path.Combine(path, "cache", i.ToString()),
          FileMode.Open))
        {
          while(fileCache.Read(bytesRecord) == LENGTH_RECORD_DB)
          {
            Account record = new()
            {
              IDAccount = bytesRecord.Take(LENGTH_ID_ACCOUNT).ToArray(),
              BlockheightAccountInit = BitConverter.ToInt32(bytesRecord, LENGTH_ID_ACCOUNT),
              Nonce = BitConverter.ToInt32(bytesRecord, LENGTH_ID_ACCOUNT + 4),
              Value = BitConverter.ToInt64(bytesRecord, LENGTH_ID_ACCOUNT + 8)
            };

            Caches[i].Add(record.IDAccount, record);
          }
        }
    }

    public void CreateImage(string path)
    {
      string pathDirectoryCache = Path.Combine(path, "cache");

      Directory.CreateDirectory(pathDirectoryCache);

      for (int i = 0; i < COUNT_CACHES; i += 1)
        Caches[i].CreateImage(
          Path.Combine(pathDirectoryCache, i.ToString()));
    }

    public void ClearCache()
    {
      Caches.ForEach(c => c.Clear());
    }

    public void Delete()
    {
      FilesDB.ForEach(f => { 
        f.Dispose();
        File.Delete(f.Name);
      });

      ClearCache();
    }

    public void InsertDB(byte[] bufferDB, int lengthDataInBuffer)
    {
      int index = 0;

      if (bufferDB[index++] == 0x00)
      {
        FileDB fileDB = new(
          Path.Combine(
            PathRootDB,
            bufferDB[index].ToString()));

        FilesDB.Add(fileDB);

        fileDB.Write(bufferDB, index, lengthDataInBuffer - 1);
      }
      else if (bufferDB[index++] == 0x01)
      {
        int indexCache = bufferDB[index++];

        while (index < lengthDataInBuffer)
        {
          Account recordDB = new();

          recordDB.BlockheightAccountInit = BitConverter.ToInt32(bufferDB, index);
          index += 4;

          recordDB.Nonce = BitConverter.ToInt32(bufferDB, index);
          index += 4;

          recordDB.Value = BitConverter.ToUInt32(bufferDB, index);
          index += 8;

          Array.Copy(bufferDB, index, recordDB.IDAccount, 0, 32);
          index += 32;

          Caches[indexCache].Add(recordDB.IDAccount, recordDB);
        }
      }
      else
        throw new ProtocolException(
          $"Invalid prefix {bufferDB[index]} in serialized DB data.");
    }

    bool TryGetCache(byte[] iDAccount, out CacheDatabaseAccounts cache)
    {
      cache = null;
      int c = IndexCache;

      while (true)
      {
        if (Caches[c].ContainsKey(iDAccount))
        {
          cache = Caches[c];
          return true;
        }

        c = (c + COUNT_CACHES - 1) % COUNT_CACHES;

        if (c == IndexCache)
          return false;
      }
    }

    public bool TryGetAccount(byte[] iDAccount, out Account account)
    {
      if (TryGetCache(iDAccount, out CacheDatabaseAccounts cache))
        if (cache.TryGetValue(iDAccount, out account))
          return true;

      FileDB fileDB = GetFileDB(iDAccount);

      if (fileDB.TryGetAccount(iDAccount, out account))
        return true;

      return false;
    }

    public void UpdateHashDatabase()
    {
      for (int i = 0; i < COUNT_CACHES; i += 1)
      {
        Caches[i].UpdateHash();

        Caches[i].Hash.CopyTo(
          HashesCaches,
          i * Caches[i].Hash.Length);
      }

      byte[] hashCaches = SHA256.ComputeHash(HashesCaches);

      for (int i = 0; i < COUNT_FILES_DB; i += 1)
      {
        FilesDB[i].UpdateHash();

        FilesDB[i].Hash.CopyTo(
          HashesFilesDB,
          i * FilesDB[i].Hash.Length);
      }

      byte[] hashFilesDB = SHA256.ComputeHash(HashesFilesDB);

      Hash = SHA256.ComputeHash(
        hashCaches.Concat(hashFilesDB).ToArray());
    }

    FileDB GetFileDB(byte[] iDAccount)
    {
      return FilesDB[iDAccount[0]];
    }

    public bool TryGetDB(byte[] hash, out byte[] dataDB)
    {
      for (int i = 0; i < HashesFilesDB.Length; i += 1)
        if (hash.HasEqualElements(HashesFilesDB, i * 32))
        {
          dataDB = new byte[FilesDB[i].Length];
          FilesDB[i].Write(dataDB, 0, dataDB.Length);
          return true;
        }

      for (int i = 0; i < HashesCaches.Length; i += 1)
        if (hash.HasEqualElements(HashesCaches, i * 32))
        {
          dataDB = Caches[i].GetBytes();
          return true;
        }

      dataDB = null;
      return false;
    }

    static void SpendAccount(TXBToken tX, Account account)
    {            
      if (account.Nonce != tX.Nonce)
        throw new ProtocolException($"Account {account} referenced by TX {tX} has unequal Nonce.");

      if (account.Value < tX.Value)
        throw new ProtocolException($"Account {account} referenced by TX {tX} does not have enough fund.");

      account.Nonce += 1;
      account.Value -= tX.Value;
    }

    public void SpendInput(TXBToken tX)
    {
      if (TryGetCache(tX.IDAccountSource, out CacheDatabaseAccounts cache))
        cache.SpendAccountInCache(tX);
      else
        GetFileDB(tX.IDAccountSource).SpendAccountInFileDB(tX);
    }

    public void InsertOutput(TXOutputBToken output, int blockHeight)
    {
      byte[] iDAccount = output.IDAccount;
      long outputValueTX = output.Value;

      int c = IndexCache;

      while (true)
      {
        if (Caches[c].TryGetValue(
          iDAccount,
          out Account account))
        {
          account.Value += outputValueTX;

          if (c != IndexCache)
          {
            Caches[c].Remove(iDAccount);
            AddToCacheIndexed(iDAccount, account);
          }

          break;
        }

        c = (c + COUNT_CACHES - 1) % COUNT_CACHES;

        if (c == IndexCache)
        {
          if (GetFileDB(iDAccount).TryFetchAccount(iDAccount, out account))
            account.Value += outputValueTX;
          else
            account = new Account
            {
              BlockheightAccountInit = blockHeight,
              Nonce = 0,
              Value = outputValueTX,
              IDAccount = iDAccount
            };

          AddToCacheIndexed(iDAccount, account);

          break;
        }
      }
    }

    void AddToCacheIndexed(byte[] iDAccount, Account account)
    {
      Caches[IndexCache].Add(iDAccount, account);

      if(Caches[IndexCache].Count > COUNT_MAX_CACHE)
      {
        for (int i = 0; i < COUNT_FILES_DB; i += 1)
          FilesDB[i].Defragment();

        IndexCache = (IndexCache + COUNT_CACHES + 1) % COUNT_CACHES;

        foreach(KeyValuePair<byte[], Account> item in Caches[IndexCache])
          GetFileDB(item.Value.IDAccount).WriteRecordDBAccount(item.Value);

        Caches[IndexCache].Clear();
      }
    }

    public long GetCountBytes()
    {
      long countBytes = 0;

      Caches.ForEach(c => countBytes += c.Count * LENGTH_RECORD_DB);
      FilesDB.ForEach(f => countBytes += f.Length);

      return countBytes;
    }
  }
}
