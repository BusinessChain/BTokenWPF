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

    string PathRootDB;
    public const int COUNT_FILES_DB = 256;
    List<FileDB> FilesDB = new();
    byte[] HashesFilesDB = new byte[COUNT_FILES_DB * 32];

    const int LENGTH_ACCOUNT = 41;
    const int LENGTH_ID_ACCOUNT = 25;

    SHA256 SHA256 = SHA256.Create();
    byte[] Hash;


    public DBAccounts(string nameToken)
    {
      PathRootDB = Path.Combine(nameToken, "FilesDB");

      for (int i = 0; i < COUNT_CACHES; i += 1)
        Caches.Add(new CacheDB());

      Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), PathRootDB));

      for (int i = 0; i < COUNT_FILES_DB; i += 1)
        FilesDB.Add(new FileDB(Path.Combine(PathRootDB, i.ToString())));
    }

    public void LoadImage(string path)
    {
      byte[] bytesAccount = new byte[LENGTH_ACCOUNT];

      for (int i = 0; i < COUNT_CACHES; i += 1)
        using (FileStream fileCache = new(
          Path.Combine(path, "cache", i.ToString()),
          FileMode.Open))
        {
          while(fileCache.Read(bytesAccount) == LENGTH_ACCOUNT)
          {
            Account account = new()
            {
              ID = bytesAccount.Take(LENGTH_ID_ACCOUNT).ToArray(),
              BlockHeightAccountInit = BitConverter.ToInt32(bytesAccount, LENGTH_ID_ACCOUNT),
              Nonce = BitConverter.ToInt32(bytesAccount, LENGTH_ID_ACCOUNT + 4),
              Value = BitConverter.ToInt64(bytesAccount, LENGTH_ID_ACCOUNT + 8)
            };

            Caches[i].Add(account.ID, account);
          }
        }
    }

    public void CreateImage(string path)
    {
      string pathDirectoryCache = Path.Combine(path, "cache");

      Directory.CreateDirectory(pathDirectoryCache);

      for (int i = 0; i < COUNT_CACHES; i += 1)
        Caches[i].CreateImage(Path.Combine(pathDirectoryCache, i.ToString()));
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
          Account account = new();

          account.BlockHeightAccountInit = BitConverter.ToInt32(bufferDB, index);
          index += 4;

          account.Nonce = BitConverter.ToInt32(bufferDB, index);
          index += 4;

          account.Value = BitConverter.ToUInt32(bufferDB, index);
          index += 8;

          Array.Copy(bufferDB, index, account.ID, 0, 32);
          index += 32;

          Caches[indexCache].Add(account.ID, account);
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
          Caches[c].Remove(account.ID);

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

    bool TryGetAccountCache(byte[] iDAccount, out Account account, bool flagRaisePriority = false)
    {
      int c = IndexCacheTopPriority;

      if (Caches[c].TryGetValue(iDAccount, out account))
        return true;

      c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;

      while (c != IndexCacheTopPriority)
      {
        if (Caches[c].TryGetValue(iDAccount, out account))
        {
          if (flagRaisePriority)
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
            ID = output.IDAccount
          };

        AddToCacheTopPriority(account);
      }
    }

    void AddToCacheTopPriority(Account account)
    {
      Caches[IndexCacheTopPriority].Add(account.ID, account);

      if(Caches[IndexCacheTopPriority].Count > COUNT_MAX_ACCOUNTS_IN_CACHE)
      {
        IndexCacheTopPriority = (IndexCacheTopPriority + 1 + COUNT_CACHES) % COUNT_CACHES;

        foreach(KeyValuePair<byte[], Account> itemInCache in Caches[IndexCacheTopPriority])
          FilesDB[itemInCache.Value.ID[0]].WriteRecordDBAccount(itemInCache.Value);

        Caches[IndexCacheTopPriority].Clear();
      }
    }
        
    public long GetCountBytes()
    {
      long countBytes = 0;

      Caches.ForEach(c => countBytes += c.Count * LENGTH_ACCOUNT);
      FilesDB.ForEach(f => countBytes += f.Length);

      return countBytes;
    }

    public List<(Account account, string locationAccount, int indexSource)> GetAccounts()
    {
      List<(Account account, string sourceObject, int indexCache)> itemsAccount = new();

      for (int i = 0; i < COUNT_CACHES; i += 1)
        foreach (Account accountInCache in Caches[i].Values)
          itemsAccount.Add((accountInCache, Caches[i].GetType().Name, i));

      for (int i = 0; i < COUNT_FILES_DB; i += 1)
        foreach (Account accountInFile in FilesDB[i].GetAccounts())
          itemsAccount.Add((accountInFile, FilesDB[i].GetType().Name, i));

      return itemsAccount;
    }
  }
}
