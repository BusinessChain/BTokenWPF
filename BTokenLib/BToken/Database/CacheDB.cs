using System;
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
        byte[] bytesCaches = new byte[Values.Count * LENGTH_ACCOUNT];

        foreach (Account account in Values)
        {
          account.ID.CopyTo(bytesCaches, i);
          i += LENGTH_ID_ACCOUNT;

          BitConverter.GetBytes(account.BlockHeightAccountInit).CopyTo(bytesCaches, i);
          i += 4;

          BitConverter.GetBytes(account.Nonce).CopyTo(bytesCaches, i);
          i += 4;

          BitConverter.GetBytes(account.Value).CopyTo(bytesCaches, i);
          i += 8;
        }

        return SHA256.HashData(bytesCaches);
      }

      public void CreateImage(string path)
      {
        using (FileStream file = new(path, FileMode.Create))
          foreach (Account account in Values)
          {
            file.Write(account.ID);
            file.Write(BitConverter.GetBytes(account.Nonce));
            file.Write(BitConverter.GetBytes(account.Value));
          }
      }
    
      public byte[] GetBytes()
      {
        byte[] dataDB = new byte[LENGTH_ACCOUNT * Count];
        int index = 0;

        foreach(Account account in Values)
        {
          BitConverter.GetBytes(account.Nonce).CopyTo(dataDB, index);
          index += 8;

          BitConverter.GetBytes(account.Value).CopyTo(dataDB, index);
          index += 8;

          account.ID.CopyTo(dataDB, index);
          index += 32;
        }

        return dataDB;
      }
    }
  }
}
