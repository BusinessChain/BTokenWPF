using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Documents;


namespace BTokenLib
{
  partial class DBAccounts
  {
    class FileDB : FileStream
    { 
      byte[] TempByteArrayCopyLastRecord = new byte[LENGTH_ACCOUNT];


      public FileDB(string path) : base(
        path,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.ReadWrite)
      {
        Seek(0, SeekOrigin.End);
      }

      public bool TryGetAccount(
        byte[] iDAccount,
        out Account account,
        bool flagRemoveAccount = false)
      {
        Seek(0, SeekOrigin.Begin);

        while (Position < Length)
        {
          int i = 0;
          while (ReadByte() == iDAccount[i++])
            if (i == LENGTH_ID_ACCOUNT)
            {
              byte[] blockheightAccountInit = new byte[4];
              Read(blockheightAccountInit);

              byte[] nonce = new byte[4];
              Read(nonce);

              byte[] value = new byte[8];
              Read(value);

              account = new()
              {
                ID = iDAccount,
                BlockHeightAccountInit = BitConverter.ToInt32(nonce),
                Nonce = BitConverter.ToInt32(nonce),
                Value = BitConverter.ToInt64(value)
              };

              if(flagRemoveAccount)
              {
                long positionCurrentRecord = Position - LENGTH_ACCOUNT;

                Position = Length - LENGTH_ACCOUNT;
                Read(TempByteArrayCopyLastRecord);

                Position = positionCurrentRecord;

                Write(TempByteArrayCopyLastRecord);

                SetLength(Length - LENGTH_ACCOUNT);

                Seek(0, SeekOrigin.End);
              }

              return true;
            }

          Position += LENGTH_ACCOUNT - Position % LENGTH_ACCOUNT;
        }

        account = null;
        return false;
      }

      public void WriteRecordDBAccount(Account account)
      {
        Write(account.ID);
        Write(BitConverter.GetBytes(account.BlockHeightAccountInit));
        Write(BitConverter.GetBytes(account.Nonce));
        Write(BitConverter.GetBytes(account.Value));
      }

      public List<Account> GetAccounts()
      {
        List<Account> account = new();

        Seek(0, SeekOrigin.Begin);

        while (Position < Length)
        {
          byte[] iDAccount = new byte[32];
          Read(iDAccount);

          byte[] blockheightAccountInit = new byte[4];
          Read(blockheightAccountInit);

          byte[] nonce = new byte[4];
          Read(nonce);

          byte[] value = new byte[8];
          Read(value);

          account.Add(new()
          {
            ID = iDAccount,
            BlockHeightAccountInit = BitConverter.ToInt32(nonce),
            Nonce = BitConverter.ToInt32(nonce),
            Value = BitConverter.ToInt64(value)
          });
        }

        return account;
      }
    }
  }
}
