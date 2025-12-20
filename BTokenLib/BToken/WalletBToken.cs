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

      List<(string address, long value, double feePerByte)> TXsQueueSend = new();

      // Die Wallet muss selber einen Account mitführen, den sie als aktuell gültig betrachtet. 
      // Zum Beispiel immer beim Versuch eine erfasste TX zu senden oder, wenn ein neue konfirmierte TX ankommt
      // wird die konsistenz zur Token DB geprüft.

      // Die Wallet über ein Interface an das Token ankoppeln.

      List<(TX tX, int ageBlock)> TXsUnconfirmedCreated = new();
      List<TX> TXsUnconfirmedReceived = new();

      public override void SendTXValue(string addressDest, long value, double feePerByte)
      {
        List<byte> tXRaw =
          Token.CreateTXValueRaw(KeyPublic, addressDest, value, feePerByte);

        SendRawTX(tXRaw);
      }

      public override void SendTXData(byte[] data, double feePerByte)
      {
        List<byte> tXRaw =
          Token.CreateTXDataRaw(KeyPublic, data, feePerByte);

        SendRawTX(tXRaw);
      }

      void SendRawTX(List<byte> tXRaw)
      {
        byte[] signature = GetSignature(tXRaw.ToArray());

        TX tX = Token.CreateTX(tXRaw, signature);

        Token.BroadcastTX(tX);

        TXsUnconfirmedCreated.Add((tX, 0));
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
            $"Exception when trying to rebroadcast yet unconfirmed transactions.".Log(this, Token.LogEntryNotifier);
          }
        }
      }

      public override void InsertTXUnconfirmed(TX tX)
      {

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