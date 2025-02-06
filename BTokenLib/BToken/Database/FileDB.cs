using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class FileDB : FileStream
  {
    byte[] TempByteArrayCopyLastRecord = new byte[Account.LENGTH_ACCOUNT];

    List<long> StartIndexesAccountsStaged = new();

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

    public bool TryGetAccountStaged(byte[] iDAccount, out AccountStaged accountStaged)
    {
      if(TryGetAccount(iDAccount, out Account account, out long startIndexAccount))
      {
        accountStaged = new AccountStaged
        {
          Account = account,
          Value = account.Value,
          Nonce = account.Nonce
        };

        StartIndexesAccountsStaged.Add(startIndexAccount);

        return true;
      }

      accountStaged = null;
      return false;
    }

    public bool TryGetAccount(byte[] iDAccount, out Account account, out long startIndexAccount)
    {
      Seek(0, SeekOrigin.Begin);

      while (Position < Length)
      {
        int i = 0;
        while (ReadByte() == iDAccount[i++])
          if (i == Account.LENGTH_ID)
          {
            Position -= Account.LENGTH_ID;

            startIndexAccount = Position;

            account = new(this);

            return true;
          }

        Position += Account.LENGTH_ACCOUNT - Position % Account.LENGTH_ACCOUNT;
      }

      account = null;
      startIndexAccount = 0;

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

    public void Commit()
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
  }
}
