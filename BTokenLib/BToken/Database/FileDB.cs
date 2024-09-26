using System;
using System.IO;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class DBAccounts
  {
    class FileDB : FileStream
    { 
      public byte[] Hash;
      bool FlagHashOutdated;

      byte[] TempByteArrayCopyLastRecord = new byte[LENGTH_RECORD_DB];

      SHA256 SHA256 = SHA256.Create();


      public FileDB(string path) : base(
        path,
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.ReadWrite)
      {
        Hash = SHA256.ComputeHash(this);
        Seek(0, SeekOrigin.End);
      }

      public bool TryGetAccount(
        byte[] iDAccount,
        out Account account,
        bool flagRemoveAccount = false)
      {
        Position = 0;

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
                IDAccount = iDAccount,
                BlockHeightAccountInit = BitConverter.ToInt32(nonce),
                Nonce = BitConverter.ToInt32(nonce),
                Value = BitConverter.ToInt64(value)
              };

              if(flagRemoveAccount)
              {
                long positionCurrentRecord = Position - LENGTH_RECORD_DB;

                Position = Length - LENGTH_RECORD_DB;
                Read(TempByteArrayCopyLastRecord);

                Position = positionCurrentRecord;

                Write(TempByteArrayCopyLastRecord);

                SetLength(Length - LENGTH_RECORD_DB);

                Seek(0, SeekOrigin.End);

                FlagHashOutdated = true;
              }

              return true;
            }

          Position += LENGTH_RECORD_DB - Position % LENGTH_RECORD_DB;
        }

        account = null;
        return false;
      }

      public void WriteRecordDBAccount(Account account)
      {
        Write(account.IDAccount);
        Write(BitConverter.GetBytes(account.BlockHeightAccountInit));
        Write(BitConverter.GetBytes(account.Nonce));
        Write(BitConverter.GetBytes(account.Value));

        FlagHashOutdated = true;
      }

      public void UpdateHash()
      {
        if (FlagHashOutdated)
          Hash = SHA256.ComputeHash(this);
      }
    }
  }
}
