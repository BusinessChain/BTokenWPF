using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class FileDB : FileStream
  {
    byte[] TempByteArrayCopyLastRecord = new byte[Account.LENGTH_ACCOUNT];

    SHA256 SHA256 = SHA256.Create();
    public byte[] Hash = new byte[32];


    public FileDB(string path) : base(
      path,
      FileMode.OpenOrCreate,
      FileAccess.ReadWrite,
      FileShare.ReadWrite)
    {
      Seek(0, SeekOrigin.End);
    }

    public bool TryGetAccount(byte[] iDAccount, out TokenBToken.Account account)
    {
      Seek(0, SeekOrigin.Begin);

      while (Position < Length)
      {
        int i = 0;
        while (ReadByte() == iDAccount[i++])
          if (i == Account.LENGTH_ID)
          {
            Position -= Account.LENGTH_ID;
            account = new(this);
            return true;
          }

        Position += Account.LENGTH_ACCOUNT - Position % Account.LENGTH_ACCOUNT;
      }

      account = null;
      return false;
    }

    public List<Account> GetAccounts()
    {
      Seek(0, SeekOrigin.Begin);

      List<Account> accounts = new();

      while (Position < Length)
        accounts.Add(new(this));

      return accounts;
    }

    int ReadInt32()
    {
      byte[] buffer = new byte[4];
      Read(buffer, 0, buffer.Length);
      return BitConverter.ToInt32(buffer);
    }

    long ReadInt64()
    {
      byte[] buffer = new byte[8];
      Read(buffer, 0, buffer.Length);
      return BitConverter.ToInt64(buffer);
    }
  }
}
