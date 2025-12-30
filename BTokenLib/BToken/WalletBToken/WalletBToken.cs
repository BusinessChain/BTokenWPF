using System;
using System.Linq;
using System.Collections.Generic;

using LiteDB;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public partial class WalletBToken : Wallet 
    {
      TokenBToken Token;

      Account AccountWalletConfirmed;
      Account AccountWalletUnconfirmed;
      int SerialNumberTX;

      List<(TX tX, int ageBlock)> TXsUnconfirmedCreated = new();
      List<TX> TXsUnconfirmedReceived = new();

      LiteDatabase Database;
      ILiteCollection<DBRecordTXWallet> DatabaseTXCollection;


      public WalletBToken(string privKeyDec, TokenBToken token)
        : base(privKeyDec)
      {
        Token = token;

        DatabaseTXCollection = Database.GetCollection<DBRecordTXWallet>("RecordsTXWallet");
      }

      public TX CreateTXCoinbase(long blockReward, int blockHeight)
      {
        List<byte> tXRaw = new()
      {
        (byte)TypesToken.Coinbase
      };

        tXRaw.AddRange(BitConverter.GetBytes(blockHeight));

        tXRaw.Add(0x01); // count outputs

        tXRaw.AddRange(BitConverter.GetBytes(blockReward));
        tXRaw.AddRange(Hash160PKeyPublic);

        return Token.ParseTX(tXRaw.ToArray(), SHA256);
      }

      public override void SendTXValue(string addressDest, long value, double feePerByte)
      {
        BuilderTXBTokenValue builderTXBTokenValue =
          new(
            this,
            KeyPublic,
            AccountWalletUnconfirmed,
            addressDest,
            value,
            feePerByte);

        // Besser wäre eigentlich, das TX Objekt direkt selber zu erstellen
        // und dann die serialisierte TXRaw zu versenden.

        TX tX = Token.ParseTX(builderTXBTokenValue.TXRaw, SHA256);

        TXsUnconfirmedCreated.Add((tX, 0));

        Token.BroadcastTX(tX);
      }

      public override void SendTXData(byte[] data, double feePerByte)
      {
        BuilderTXBTokenData tXBuilder = new(
          this,
          KeyPublic,
          AccountWalletUnconfirmed,
          data,
          feePerByte);

        TX tX = Token.ParseTX(tXBuilder.TXRaw, SHA256);

        TXsUnconfirmedCreated.Add((tX, 0));

        Token.BroadcastTX(tX);
      }

      public override void InsertBlock(Block block)
      {
        foreach (TXBToken tX in block.TXs)
        {
          bool flagIndexTX = false;

          if (tX.IDAccountSource.IsAllBytesEqual(Hash160PKeyPublic))
            flagIndexTX = true;

          foreach (TXOutputBToken output in tX.TXOutputs)
            if (output.IDAccount.IsAllBytesEqual(Hash160PKeyPublic))
              flagIndexTX = true;

          if (flagIndexTX)
            DatabaseTXCollection.Upsert(
              new DBRecordTXWallet()
              {
                HashTX = tX.Hash,
                BlockHeightOriginTX = block.Header.Height,
                SerialNumberTX = SerialNumberTX++,
                TXRaw = tX.TXRaw
              });

          TXsUnconfirmedCreated.RemoveAll(tXUnconfirmed => tXUnconfirmed.tX.Hash.IsAllBytesEqual(tX.Hash));
        }

        foreach ((TXBToken tX, int) tXBToken in TXsUnconfirmedCreated.Where(t => t.ageBlock > 3))
        {
          try
          {
            Token.BroadcastTX(tXBToken.tX);
          }
          catch (Exception ex)
          {
            $"{ex.GetType().Name} when trying to rebroadcast unconfirmed transactions:\n {ex.Message}.".Log(this, Token.LogEntryNotifier);
          }
        }
      }

      public override void ReverseBlock(Block block)
      {

      }

      public override long GetBalance()
      {
        try
        {
          Account accountUnconfirmed = Token.GetAccountUnconfirmed(Hash160PKeyPublic);
          return accountUnconfirmed.Balance;
        }
        catch
        {
          return 0;
        }
      }
    }
  }
}