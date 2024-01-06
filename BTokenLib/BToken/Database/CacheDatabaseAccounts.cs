using Org.BouncyCastle.X509;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BTokenLib
{
  partial class DatabaseAccounts
  {
    class CacheDatabaseAccounts : Dictionary<byte[], Account>
    {
      public byte[] Hash;
      SHA256 SHA256 = SHA256.Create();

      public CacheDatabaseAccounts() 
        : base(new EqualityComparerByteArray())
      { }


      public void SpendAccountInCache(TXBToken tX)
      {
        TryGetValue(tX.IDAccount, out Account account);

        SpendAccount(tX, account);

        if (account.Value == 0)
          Remove(tX.IDAccount);
      }

      public void UpdateHash()
      {
        int i = 0;
        byte[] bytesCaches = new byte[Values.Count * LENGTH_RECORD_DB];

        foreach (Account record in Values)
        {
          record.IDAccount.CopyTo(bytesCaches, i);
          i += LENGTH_ID_ACCOUNT;

          BitConverter.GetBytes(record.Nonce).CopyTo(bytesCaches, i);
          i += LENGTH_NONCE;

          BitConverter.GetBytes(record.Value).CopyTo(bytesCaches, i);
          i += LENGTH_VALUE;
        }

        Hash = SHA256.ComputeHash(bytesCaches);
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
