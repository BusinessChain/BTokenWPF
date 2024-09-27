using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public partial class DBAccounts
  {
    public const int COUNT_CACHES = 256;
    byte[] HashesCaches = new byte[COUNT_CACHES * 32];
    const int COUNT_MAX_ACCOUNTS_IN_CACHE = 40000; // Read from configuration file
    List<CacheDB> Caches = new();
    int IndexCacheTopPriority;

    const string PathRootDB = "FilesDB";
    public const int COUNT_FILES_DB = 256; // file index will be idAccount[0]
    List<FileDB> FilesDB = new();
    byte[] HashesFilesDB = new byte[COUNT_FILES_DB * 32];

    const int LENGTH_RECORD_DB = 41;
    const int LENGTH_ID_ACCOUNT = 25;

    SHA256 SHA256 = SHA256.Create();
    byte[] Hash;


    public DBAccounts()
    {
      for (int i = 0; i < COUNT_CACHES; i += 1)
        Caches.Add(new CacheDB());

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
              BlockHeightAccountInit = BitConverter.ToInt32(bytesRecord, LENGTH_ID_ACCOUNT),
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

          recordDB.BlockHeightAccountInit = BitConverter.ToInt32(bufferDB, index);
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

    public bool TryGetAccount(byte[] iDAccount, out Account account)
    {
      if (TryGetAccountCache(iDAccount, out account))
        return true;
      else if (FilesDB[iDAccount[0]].TryGetAccount(iDAccount, out account))
        return true;

      return false;
    }

    public void UpdateHashDatabase()
    {
      for (int i = 0; i < COUNT_CACHES; i += 1)
      {
        byte[] hashCache = Caches[i].ComputeHash();
        hashCache.CopyTo(HashesCaches, i * hashCache.Length);
      }

      byte[] hashCaches = SHA256.ComputeHash(HashesCaches);

      for (int i = 0; i < COUNT_FILES_DB; i += 1)
      {
        byte[] hashFile = SHA256.ComputeHash(FilesDB[i]);
        hashFile.CopyTo(HashesFilesDB, i * hashFile.Length);
      }

      byte[] hashFilesDB = SHA256.ComputeHash(HashesFilesDB);

      Hash = SHA256.ComputeHash(
        hashCaches.Concat(hashFilesDB).ToArray());
    }

    public bool TryGetDB(byte[] hash, out byte[] dataDB)
    {
      for (int i = 0; i < HashesFilesDB.Length; i += 1)
        if (hash.IsAllBytesEqual(HashesFilesDB, i * 32))
        {
          dataDB = new byte[FilesDB[i].Length];
          FilesDB[i].Write(dataDB, 0, dataDB.Length);
          return true;
        }

      for (int i = 0; i < HashesCaches.Length; i += 1)
        if (hash.IsAllBytesEqual(HashesCaches, i * 32))
        {
          dataDB = Caches[i].GetBytes();
          return true;
        }

      dataDB = null;
      return false;
    }

    public void SpendInput(TXBToken tX)
    {
      if (TrySpendAccountCache(tX))
        return;
      
      if (FilesDB[tX.IDAccountSource[0]].TryGetAccount(
        tX.IDAccountSource, 
        out Account account,
        flagRemoveAccount: true))
      {
        account.SpendTX(tX);

        if (account.Value > 0)
          AddToCacheTopPriority(account);

        return;
      }

      throw new ProtocolException(
        $"Account {tX.IDAccountSource.ToHexString()} referenced by TX\n" +
        $"{tX.Hash.ToHexString()} not found in database.");
    }

    bool TrySpendAccountCache(TXBToken tX)
    {
      int c = IndexCacheTopPriority;

      if (Caches[c].TryGetValue(tX.IDAccountSource, out Account account))
      {
        account.SpendTX(tX);

        if (account.Value == 0)
          Caches[c].Remove(account.IDAccount);

        return true;
      }

      c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;

      while (c != IndexCacheTopPriority)
      {
        if (Caches[c].Remove(tX.IDAccountSource, out account))
        {
          account.SpendTX(tX);

          if (account.Value > 0)
            AddToCacheTopPriority(account);

          return true;
        }

        c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;
      }

      return false;
    }

    bool TryGetAccountCache(
      byte[] iDAccount, 
      out Account account, 
      bool flagRaisePriority = false)
    {
      int c = IndexCacheTopPriority;

      if (Caches[c].TryGetValue(iDAccount, out account))
        return true;

      c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;

      while (c != IndexCacheTopPriority)
      {
        if (Caches[c].TryGetValue(iDAccount, out account))
        {
          if(flagRaisePriority)
          {
            Caches[c].Remove(iDAccount);
            AddToCacheTopPriority(account);
          }

          return true;
        }

        c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;
      }

      return false;
    }

    public void InsertOutput(TXOutputBToken output, int blockHeight)
    {
      if (TryGetAccountCache(output.IDAccount, out Account account, flagRaisePriority: true))
        account.Value += output.Value;
      else
      {
        if (FilesDB[output.IDAccount[0]].TryGetAccount(output.IDAccount, out account, flagRemoveAccount: true))
          account.Value += output.Value;
        else
          account = new Account
          {
            BlockHeightAccountInit = blockHeight,
            Nonce = 0,
            Value = output.Value,
            IDAccount = output.IDAccount
          };

        AddToCacheTopPriority(account);
      }
    }

    void AddToCacheTopPriority(Account account)
    {
      Caches[IndexCacheTopPriority].Add(account.IDAccount, account);

      if(Caches[IndexCacheTopPriority].Count > COUNT_MAX_ACCOUNTS_IN_CACHE)
      {
        IndexCacheTopPriority = (IndexCacheTopPriority + 1 + COUNT_CACHES) % COUNT_CACHES;

        foreach(KeyValuePair<byte[], Account> itemInCache in Caches[IndexCacheTopPriority])
          FilesDB[itemInCache.Value.IDAccount[0]].WriteRecordDBAccount(itemInCache.Value);

        Caches[IndexCacheTopPriority].Clear();
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
