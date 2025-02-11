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

    public bool TryGetAccount(byte[] iDAccount, out Account account)
    {
      Seek(0, SeekOrigin.Begin);

      while (Position < Length)
      {
        int i = 0;
        while (ReadByte() == iDAccount[i++])
          if (i == Account.LENGTH_ID)
          {
            Position -= Account.LENGTH_ID;
            account = ParseAccount();
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
        accounts.Add(ParseAccount());

      return accounts;
    }

    Account ParseAccount()
    {
      Account account = new();

      account.FileDBOrigin = this;
      account.StartIndexFileDBOrigin = Position;

      Read(account.ID, 0, account.ID.Length);
      account.BlockHeightAccountInit = ReadInt32();
      account.Nonce = ReadInt32();
      account.Value = ReadInt64();

      return account;
    }

    public void Commit(List<Account> accounts)
    {
      foreach(long positionStartAccount in StartIndexesAccountsStaged)
      {
        Position = Length - Account.LENGTH_ACCOUNT;

        if(positionStartAccount != Position)
        {
          Read(TempByteArrayCopyLastRecord);

          Position = positionStartAccount;

          Write(TempByteArrayCopyLastRecord);
        }
      }

      SetLength(Length - Account.LENGTH_ACCOUNT * StartIndexesAccountsStaged.Count);
      Position = Length;

      Hash = SHA256.ComputeHash(this);

      StartIndexesAccountsStaged.Clear();
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
