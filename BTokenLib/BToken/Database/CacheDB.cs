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

      public byte[] GetBytes()
      {
        int startIndex = 0;
        byte[] buffer = new byte[Account.LENGTH_ACCOUNT * Count];

        foreach (Account account in Values)
          account.Serialize(buffer, ref startIndex);

        return buffer;
      }

      public void CreateImage(string path)
      {
        using (FileStream file = new(path, FileMode.Create))
          foreach (Account account in Values)
            file.Write(account.Serialize());
      }
    }
  }
}
