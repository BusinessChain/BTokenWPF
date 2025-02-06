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

    SHA256 SHA256 = SHA256.Create();
    byte[] Hash;

    Dictionary<byte[], AccountStaged> AccountsStaged = new(new EqualityComparerByteArray());


    public DBAccounts(string nameToken)
    {
      PathRootDB = Path.Combine(nameToken, "FilesDB");

      for (int i = 0; i < COUNT_CACHES; i++)
        Caches.Add(new CacheDB());

      Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), PathRootDB));

      for (int i = 0; i < COUNT_FILES_DB; i++)
        FilesDB.Add(new FileDB(Path.Combine(PathRootDB, i.ToString())));
    }

    public void LoadImage(string path)
    {
      for (int i = 0; i < COUNT_CACHES; i++)
      {
        int startIndex = 0;
        byte[] buffer = File.ReadAllBytes(Path.Combine(path, "cache", i.ToString()));

        while (startIndex < buffer.Length)
        {
          Account account = new(buffer, ref startIndex);
          Caches[i].Add(account.ID, account);
        }
      }
    }

    public void CreateImage(string path)
    {
      string pathDirectoryCache = Path.Combine(path, "cache");

      Directory.CreateDirectory(pathDirectoryCache);

      for (int i = 0; i < COUNT_CACHES; i++)
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

    public void InsertDB(byte[] buffer, int lengthDataInBuffer)
    {
      int startIndex = 0;

      byte dBType = buffer[startIndex++];

      if (dBType == 0x00)
      {
        FileDB fileDB = new(Path.Combine(PathRootDB, buffer[startIndex++].ToString()));

        FilesDB.Add(fileDB);

        fileDB.Write(buffer, startIndex, lengthDataInBuffer - 1);
      }
      else if (dBType == 0x01)
      {
        int indexCache = buffer[startIndex++];

        while (startIndex < lengthDataInBuffer)
        {
          Account account = new(buffer, ref startIndex);
          Caches[indexCache].Add(account.ID, account);
        }
      }
      else
        throw new ProtocolException(
          $"Invalid prefix {buffer[startIndex]} in serialized DB data.");
    }

    public bool TryGetAccount(byte[] iDAccount, out Account account)
    {
      if (TryGetCacheContainingAccount(iDAccount, out CacheDB cache))
      {
        account = cache[iDAccount];
        return true;
      }

      return FilesDB[iDAccount[0]].TryGetAccount(iDAccount, out account, out long startIndexAccount);
    }

    public void UpdateHashDatabase()
    {
      for (int i = 0; i < COUNT_CACHES; i++)
      {
        byte[] hashCache = SHA256.ComputeHash(Caches[i].GetBytes());
        hashCache.CopyTo(HashesCaches, i * hashCache.Length);
      }

      byte[] hashCaches = SHA256.ComputeHash(HashesCaches);

      for (int i = 0; i < COUNT_FILES_DB; i++)
        FilesDB[i].Hash.CopyTo(HashesFilesDB, i * 32);

      byte[] hashFilesDB = SHA256.ComputeHash(HashesFilesDB);

      Hash = SHA256.ComputeHash(hashCaches.Concat(hashFilesDB).ToArray());
    }

    public bool TryGetDB(byte[] hash, out byte[] dataDB)
    {
      for (int i = 0; i < HashesFilesDB.Length; i++)
        if (hash.IsAllBytesEqual(HashesFilesDB, i * 32))
        {
          dataDB = new byte[FilesDB[i].Length];
          FilesDB[i].Write(dataDB, 0, dataDB.Length);
          return true;
        }

      for (int i = 0; i < HashesCaches.Length; i++)
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
      if(!AccountsStaged.TryGetValue(tX.IDAccountSource, out AccountStaged accountStaged))
      {
        if (!TryGetAccountStagedFromCache(tX.IDAccountSource, out accountStaged))
          if (!FilesDB[tX.IDAccountSource[0]].TryGetAccountStaged(tX.IDAccountSource, out accountStaged))
            throw new ProtocolException(
              $"Account {tX.IDAccountSource.ToHexString()} referenced by TX {tX} not found in database.");

        AccountsStaged.Add(tX.IDAccountSource, accountStaged);
      }

      accountStaged.SpendTX(tX);
    }

    bool TryGetAccountStagedFromCache(byte[] iDAccount, out AccountStaged accountStaged)
    {
      if(TryGetCacheContainingAccount(iDAccount, out CacheDB cache))
      {
        Account account = cache[iDAccount];

        accountStaged = new AccountStaged
        {
          Account = account,
          CacheDB = cache,
          Value = account.Value,
          Nonce = account.Nonce
        };

        return true;
      }

      accountStaged = null;
      return false;
    }

    bool TryGetCacheContainingAccount(byte[] iDAccount, out CacheDB cache)
    {
      int c = IndexCacheTopPriority;

      do
      {
        cache = Caches[c];

        if (cache.ContainsKey(iDAccount))
          return true;

        c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;
      } while (c != IndexCacheTopPriority);

      cache = null;
      return false;
    }

    public void InsertOutput(TXOutputBToken output, int blockHeight)
    {
      if(!AccountsStaged.TryGetValue(output.IDAccount, out AccountStaged accountStaged))
      {
        if (!TryGetAccountStagedFromCache(output.IDAccount, out accountStaged))
          if (!FilesDB[output.IDAccount[0]].TryGetAccountStaged(output.IDAccount, out accountStaged))
            accountStaged = new AccountStaged
            {
              Account = new Account
              {
                ID = output.IDAccount,
                BlockHeightAccountInit = blockHeight,
                Value = output.Value
              }
            };

        AccountsStaged.Add(output.IDAccount, accountStaged);
      }

      accountStaged.AddValue(output.Value);
    }

    public void PurgeStagedData()
    {
      AccountsStaged.Clear();
    }

    public void Commit()
    {
      FilesDB.ForEach(f => f.Commit());
      Caches.ForEach(c => c.Commit());

      foreach (AccountStaged accountStaged in AccountsStaged.Values)
      {
        Account account = accountStaged.Account;

        accountStaged.CacheDB?.Remove(account.ID);

        accountStaged.FileDB?.Commit();

        if (account.Value > 0)
          AddToCacheTopPriority(account);
      }

      UpdateHashDatabase();

      AccountsStaged.Clear();
    }

    void AddToCacheTopPriority(Account account)
    {
      CacheDB cacheTopPriority = Caches[IndexCacheTopPriority];

      cacheTopPriority.Add(account.ID, account);

      if(cacheTopPriority.Count > COUNT_MAX_ACCOUNTS_IN_CACHE)
      {
        IndexCacheTopPriority = (IndexCacheTopPriority + 1 + COUNT_CACHES) % COUNT_CACHES;

        cacheTopPriority = Caches[IndexCacheTopPriority];

        foreach (KeyValuePair<byte[], Account> itemInCache in cacheTopPriority)
          itemInCache.Value.Serialize(FilesDB[itemInCache.Value.ID[0]]);

        cacheTopPriority.Clear();
      }
    }
        
    public long GetCountBytes()
    {
      long countBytes = 0;

      Caches.ForEach(c => countBytes += c.Count * Account.LENGTH_ACCOUNT);
      FilesDB.ForEach(f => countBytes += f.Length);

      return countBytes;
    }

    public List<(Account account, string locationAccount, int indexSource)> GetAccounts()
    {
      List<(Account account, string sourceObject, int indexCache)> itemsAccount = new();

      for (int i = 0; i < COUNT_CACHES; i++)
        foreach (Account accountInCache in Caches[i].Values)
          itemsAccount.Add((accountInCache, Caches[i].GetType().Name, i));

      for (int i = 0; i < COUNT_FILES_DB; i++)
        foreach (Account accountInFile in FilesDB[i].GetAccounts())
          itemsAccount.Add((accountInFile, FilesDB[i].GetType().Name, i));

      return itemsAccount;
    }
  }
}
