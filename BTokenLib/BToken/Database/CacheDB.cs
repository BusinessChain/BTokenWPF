﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  partial class DBAccounts
  {
    class CacheDB : Dictionary<byte[], Account>
    {
      public CacheDB() 
        : base(new EqualityComparerByteArray())
      { }

      public void SpendAccountInCache(TXBToken tX)
      {
        TryGetValue(tX.IDAccountSource, out Account account);

        account.SpendTX(tX);

        if (account.Value == 0)
          Remove(tX.IDAccountSource);
      }

      public byte[] ComputeHash()
      {
        int i = 0;
        byte[] bytesCaches = new byte[Values.Count * LENGTH_RECORD_DB];

        foreach (Account record in Values)
        {
          record.IDAccount.CopyTo(bytesCaches, i);
          i += LENGTH_ID_ACCOUNT;

          BitConverter.GetBytes(record.BlockHeightAccountInit).CopyTo(bytesCaches, i);
          i += 4;

          BitConverter.GetBytes(record.Nonce).CopyTo(bytesCaches, i);
          i += 4;

          BitConverter.GetBytes(record.Value).CopyTo(bytesCaches, i);
          i += 8;
        }

        return SHA256.HashData(bytesCaches);
      }

      public void CreateImage(string path)
      {
        using (FileStream file = new(path, FileMode.Create))
          foreach (Account record in Values)
          {
            file.Write(record.IDAccount);
            file.Write(BitConverter.GetBytes(record.Nonce));
            file.Write(BitConverter.GetBytes(record.Value));
          }
      }
    
      public byte[] GetBytes()
      {
        byte[] dataDB = new byte[LENGTH_RECORD_DB * Count];
        int index = 0;

        foreach(Account recordDB in Values)
        {
          BitConverter.GetBytes(recordDB.Nonce).CopyTo(dataDB, index);
          index += 8;

          BitConverter.GetBytes(recordDB.Value).CopyTo(dataDB, index);
          index += 8;

          recordDB.IDAccount.CopyTo(dataDB, index);
          index += 32;
        }

        return dataDB;
      }
    }
  }
}
