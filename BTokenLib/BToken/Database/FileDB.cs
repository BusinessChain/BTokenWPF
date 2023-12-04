﻿using System;
using System.IO;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class DatabaseAccounts
  {
    class FileDB : FileStream
    { 
      int TresholdRatioDefragmentation = 10;
      int CountRecords;
      int CountRecordsNullyfied;

      public byte[] Hash;
      bool FlagHashOutdated;
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

      public void SpendAccountInFileDB(TXBToken tX)
      {
        Position = 0;

        while (Position < Length)
        {
          int i = 0;

          while (ReadByte() == tX.IDAccount[i++])
            if (i == LENGTH_ID_ACCOUNT)
            {
              byte[] nonce = new byte[8];
              Read(nonce);

              byte[] value = new byte[8];
              Read(value);

              Account account = new()
              {
                IDAccount = tX.IDAccount,
                Nonce = BitConverter.ToUInt64(nonce),
                Value = BitConverter.ToInt64(value)
              };

              SpendAccount(tX, account);

              if (account.Value > 0)
              {
                Position -= LENGTH_NONCE + LENGTH_VALUE;
                Write(BitConverter.GetBytes(account.Nonce));
                Write(BitConverter.GetBytes(account.Value));
              }
              else
              {
                Position -= LENGTH_RECORD_DB;
                Write(new byte[LENGTH_RECORD_DB]);

                CountRecordsNullyfied += 1;
              }

              FlagHashOutdated = true;
              return;
            }

          Position += LENGTH_RECORD_DB - Position % LENGTH_RECORD_DB;
        }

        throw new ProtocolException(
          $"Account {tX.IDAccount.ToHexString()} referenced by TX\n" +
          $"{tX.Hash.ToHexString()} not found in database.");
      }

      public bool TryFetchAccount(
        byte[] iDAccount,
        out Account account)
      {
        Position = 0;

        while (Position < Length)
        {
          int i = 0;
          while (ReadByte() == iDAccount[i++])
            if (i == LENGTH_ID_ACCOUNT)
            {
              byte[] nonce = new byte[8];
              Read(nonce);

              byte[] value = new byte[8];
              Read(value);

              account = new()
              {
                IDAccount = iDAccount,
                Nonce = BitConverter.ToUInt64(nonce),
                Value = BitConverter.ToInt64(value)
              };

              Position -= LENGTH_RECORD_DB;
              Write(new byte[LENGTH_RECORD_DB]);

              CountRecordsNullyfied += 1;
              FlagHashOutdated = true;

              return true;
            }

          Position += LENGTH_RECORD_DB - Position % LENGTH_RECORD_DB;
        }

        account = null;
        return false;
      }

      public void WriteRecordDBAccount(Account account)
      {
        Seek(Position, SeekOrigin.End);

        Write(account.IDAccount);
        Write(BitConverter.GetBytes(account.Nonce));
        Write(BitConverter.GetBytes(account.Value));

        CountRecords += 1;

        FlagHashOutdated = true;
      }

      public void Defragment()
      {
        if(CountRecords / CountRecordsNullyfied < TresholdRatioDefragmentation)
        {
          Position = 0;
          byte[] bytesFileDB = new byte[Length];
          Read(bytesFileDB, 0, (int)Length);

          Position = 0;
          Flush();

          for (int i = 0; i < bytesFileDB.Length; i += LENGTH_RECORD_DB)
          {
            int j = 0;
            while (bytesFileDB[i + j] == 0 && j < LENGTH_ID_ACCOUNT)
              j += 1;

            if(j < LENGTH_ID_ACCOUNT)
              Write(bytesFileDB, i, LENGTH_RECORD_DB);
          }

          Flush();

          FlagHashOutdated = true;
        }
      }

      public void UpdateHash()
      {
        if (FlagHashOutdated)
          Hash = SHA256.ComputeHash(this);
      }
    }
  }
}
