using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  //public partial class DBAccounts
  //{
  //  // New cache concept


  //  //

  //  public const int COUNT_CACHES = 256;
  //  const int COUNT_MAX_ACCOUNTS_IN_CACHE = 40000; // Read from configuration file
  //  List<Dictionary<byte[], Account>> Caches = new();
  //  int IndexCacheTopPriority;

  //  string PathRootDB;
  //  public const int COUNT_FILES_DB = 256;
  //  List<FileDB> FilesDB = new();
  //  byte[] HashesFilesDB = new byte[COUNT_FILES_DB * 32];

  //  SHA256 SHA256 = SHA256.Create();
  //  byte[] Hash;

  //  Dictionary<byte[], Account> AccountsStaged;


  //  public DBAccounts(string nameToken)
  //  {
  //    PathRootDB = Path.Combine(nameToken, "FilesDB");

  //    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), PathRootDB));

  //    AccountsStaged = new(new EqualityComparerByteArray());

  //    for (int i = 0; i < COUNT_FILES_DB; i++)
  //      FilesDB.Add(new FileDB(Path.Combine(PathRootDB, i.ToString())));

  //    for (int i = 0; i < COUNT_CACHES; i++)
  //      Caches.Add(new Dictionary<byte[], Account>(new EqualityComparerByteArray()));
  //  }

  //  public void ClearCache()
  //  {
  //    Caches.ForEach(c => c.Clear());
  //  }

  //  public void Delete()
  //  {
  //    FilesDB.ForEach(f => { 
  //      f.Dispose();
  //      File.Delete(f.Name);
  //    });

  //    ClearCache();
  //  }

  //  public void InsertDB(byte[] buffer, int lengthDataInBuffer)
  //  {
  //    int startIndex = 0;

  //    FileDB fileDB = new(Path.Combine(PathRootDB, buffer[startIndex].ToString()));
  //    fileDB.Write(buffer, startIndex, lengthDataInBuffer - 1);

  //    FilesDB.Add(fileDB);
  //  }

  //  public bool TryGetAccount(byte[] iDAccount, out Account account)
  //  {
  //    account = null;

  //    if (!TryPopAccountFromCache(iDAccount, out account))
  //      if (!FilesDB[iDAccount[0]].TryGetAccount(iDAccount, out account))
  //        return false;

  //    AddToCacheTopPriority(account);
  //    return true;
  //  }

  //  public bool TryGetDB(byte[] hash, out byte[] dataDB)
  //  {
  //    for (int i = 0; i < HashesFilesDB.Length; i++)
  //      if (hash.IsAllBytesEqual(HashesFilesDB, i * 32))
  //      {
  //        dataDB = new byte[FilesDB[i].Length];
  //        FilesDB[i].Write(dataDB, 0, dataDB.Length);
  //        return true;
  //      }

  //    dataDB = null;
  //    return false;
  //  }

  //  public void SpendInput(TXBToken tX)
  //  {
  //    if (tX is TXBTokenCoinbase)
  //      return;

  //    Account accountStaged;

  //    if (!AccountsStaged.TryGetValue(tX.IDAccountSource, out accountStaged))
  //      if (!TryPopAccountFromCache(tX.IDAccountSource, out accountStaged))
  //        if (!FilesDB[tX.IDAccountSource[0]].TryGetAccount(tX.IDAccountSource, out accountStaged))
  //          throw new ProtocolException($"Account {tX.IDAccountSource.ToHexString()} referenced by TX {tX} not found in database.");

  //    accountStaged.SpendTX(tX);
  //    AccountsStaged.Add(tX.IDAccountSource, accountStaged);
  //  }

  //  public void InsertOutput(TXOutputBToken output, int blockHeight)
  //  {
  //    if(output.Value <= 0)
  //      throw new ProtocolException($"Value of TX output funding {output.IDAccount.ToHexString()} is not greater than zero.");

  //    Account accountStaged;

  //    if (!AccountsStaged.TryGetValue(output.IDAccount, out accountStaged))
  //      if (!TryPopAccountFromCache(output.IDAccount, out accountStaged))
  //        if (!FilesDB[output.IDAccount[0]].TryGetAccount(output.IDAccount, out accountStaged))
  //          accountStaged = new()
  //          {
  //            ID = output.IDAccount,
  //            BlockHeightAccountInit = blockHeight,
  //            Balance = output.Value
  //          };

  //    accountStaged.Balance += output.Value;
  //    AccountsStaged.Add(output.IDAccount, accountStaged);
  //  }

  //  public void ReverseSpendInput(TXBToken tX)
  //  {
  //    if (!AccountsStaged.TryGetValue(tX.IDAccountSource, out Account accountStaged))
  //    {
  //      if (!TryPopAccountFromCache(tX.IDAccountSource, out accountStaged))
  //        if (!FilesDB[tX.IDAccountSource[0]].TryGetAccount(tX.IDAccountSource, out accountStaged))
  //          accountStaged = new()
  //          {
  //            ID = tX.IDAccountSource,
  //            BlockHeightAccountInit = tX.BlockheightAccountInit,
  //            Nonce = tX.Nonce,
  //          };

  //      AccountsStaged.Add(tX.IDAccountSource, accountStaged);
  //    }

  //    accountStaged.ReverseSpendTX(tX);
  //  }

  //  public void ReverseOutput(TXOutputBToken output)
  //  {
  //    if (!AccountsStaged.TryGetValue(output.IDAccount, out Account accountStaged))
  //    {
  //      if (!TryPopAccountFromCache(output.IDAccount, out accountStaged))
  //        if (!FilesDB[output.IDAccount[0]].TryGetAccount(output.IDAccount, out accountStaged))
  //          throw new ProtocolException($"TX Output cannot be reversed because account {output.IDAccount.ToHexString()} does not exist in database.");

  //      AccountsStaged.Add(output.IDAccount, accountStaged);
  //    }

  //    accountStaged.Balance -= output.Value;
  //  }

  //  bool TryPopAccountFromCache(byte[] iDAccount, out Account account)
  //  {
  //    int c = IndexCacheTopPriority;

  //    do
  //    {
  //      Dictionary<byte[], Account> cache = Caches[c];

  //      if (cache.TryGetValue(iDAccount, out account))
  //      {
  //        cache.Remove(iDAccount);
  //        return true;
  //      }

  //      c = (c - 1 + COUNT_CACHES) % COUNT_CACHES;
  //    } while (c != IndexCacheTopPriority);

  //    account = null;
  //    return false;
  //  }

  //  public void PurgeStagedData()
  //  {
  //    AccountsStaged.Clear();
  //  }

  //  void AddToCacheTopPriority(Account account)
  //  {
  //    Dictionary<byte[], Account> cacheTopPriority = Caches[IndexCacheTopPriority];

  //    if(cacheTopPriority.Count == COUNT_MAX_ACCOUNTS_IN_CACHE)
  //    {
  //      IndexCacheTopPriority = (IndexCacheTopPriority + 1 + COUNT_CACHES) % COUNT_CACHES;

  //      cacheTopPriority = Caches[IndexCacheTopPriority];

  //      cacheTopPriority.Clear();
  //    }

  //    cacheTopPriority.Add(account.ID, account);
  //  }
        
  //  public List<(Account account, string locationAccount, int indexSource)> GetAccounts()
  //  {
  //    List<(Account account, string sourceObject, int indexCache)> itemsAccount = new();

  //    for (int i = 0; i < COUNT_CACHES; i++)
  //      foreach (Account accountInCache in Caches[i].Values)
  //        itemsAccount.Add((accountInCache, Caches[i].GetType().Name, i));

  //    for (int i = 0; i < COUNT_FILES_DB; i++)
  //      foreach (Account accountInFile in FilesDB[i].GetAccounts())
  //        itemsAccount.Add((accountInFile, FilesDB[i].GetType().Name, i));

  //    return itemsAccount;
  //  }
  //}
}
