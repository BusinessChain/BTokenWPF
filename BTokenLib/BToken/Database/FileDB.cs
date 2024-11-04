using System;
using System.IO;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class DBAccounts
  {
    class FileDB : FileStream
    { 
      byte[] TempByteArrayCopyLastRecord = new byte[Account.LENGTH_ACCOUNT];


      public FileDB(string path) : base(
        path,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.ReadWrite)
      {
        Seek(0, SeekOrigin.End);
      }

      public bool TryGetAccount(byte[] iDAccount, out Account account, bool flagRemoveAccount = false)
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

              if (flagRemoveAccount)
              {
                long positionCurrentRecord = Position - Account.LENGTH_ACCOUNT;

                Position = Length - Account.LENGTH_ACCOUNT;
                Read(TempByteArrayCopyLastRecord);

                Position = positionCurrentRecord;

                Write(TempByteArrayCopyLastRecord);

                SetLength(Length - Account.LENGTH_ACCOUNT);
                Seek(0, SeekOrigin.End);
              }

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
    }
  }
}
