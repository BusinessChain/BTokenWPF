using System;
using System.IO;
using System.Collections.Generic;

namespace BTokenLib
{
  public class CacheDB : Dictionary<byte[], Account>
  {
    public CacheDB()
      : base(new EqualityComparerByteArray())
    { }

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
