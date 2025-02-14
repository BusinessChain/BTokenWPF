using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Diagnostics.Eventing.Reader;
using System.Linq;


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

    public void Commit(List<Account> accounts)
    {
      List<Account> accountsUpdate = new();
      List<Account> accountsRemove = new();
      List<Account> accountsNew = new();

      foreach (Account account in accounts)
      {
        if (account.Value > 0)
        {
          if (account.StartIndexFileDBOrigin > 0)
            accountsUpdate.Add(account);
          else
            accountsNew.Add(account);
        }
        else
          accountsRemove.Add(account);
      }

      foreach(Account accountUpdate in accountsUpdate)
      {
        Position = accountUpdate.StartIndexFileDBOrigin;
        Write(accountUpdate.Serialize());
      }

      int i = 0;

      while (i < accountsNew.Count)
      {
        if (accountsRemove.Count > i)
          Position = accountsRemove[i].StartIndexFileDBOrigin;
        else
          Position = Length;

        accountsNew[i].StartIndexFileDBOrigin = Position;
        Write(accountsNew[i].Serialize());

        i += 1;
      }

      long lengthFileStream = Length;

      while (i < accountsRemove.Count)
      {
        Position = lengthFileStream - Account.LENGTH_ACCOUNT;

        if (accountsRemove[i].StartIndexFileDBOrigin != Position)
        {
          Read(TempByteArrayCopyLastRecord);

          Position = accountsRemove[i].StartIndexFileDBOrigin;

          Write(TempByteArrayCopyLastRecord);
        }

        lengthFileStream -= Account.LENGTH_ACCOUNT;

        i += 1;
      }

      SetLength(lengthFileStream);
      Position = Length;

      Hash = SHA256.ComputeHash(this);

      Flush();
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
