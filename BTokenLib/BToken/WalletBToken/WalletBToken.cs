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

      public const byte OP_RETURN = 0x6A;


      public WalletBToken(string privKeyDec, TokenBToken token)
        : base(privKeyDec)
      {
        Token = token;

        DatabaseTXCollection = Database.GetCollection<DBRecordTXWallet>("RecordsTXWallet");
      }

      public TX CreateTXCoinbase(long blockReward, int blockHeight)
      {
        TXBToken tX = new()
        {
          KeyPublic = new byte[32],
          BlockheightAccountCreated = blockHeight,
        };

        TXOutputBToken tXOutput = new()
        {
          Type = TXOutputBToken.TypesToken.P2PKH,
          Script = BitConverter.GetBytes(blockReward).Concat(Hash160PKeyPublic).ToArray()
        };

        tX.Serialize();

        return tX;
      }

      public override void SendTXValue(
        string addressDest,
        long value,
        double feePerByte,
        int sequence)
      {
        TXOutputBToken tXOutput = new()
        {
          Type = TXOutputBToken.TypesToken.P2PKH,
          Script = BitConverter.GetBytes(value).Concat(addressDest.Base58CheckToPubKeyHash()).ToArray()
        };

        SendTX(tXOutput, feePerByte);
      }

      public override void SendTXData(
        byte[] data,
        double feePerByte,
        int sequence)
      {
        TXOutputBToken tXOutput = new()
        {
          Type = TXOutputBToken.TypesToken.Data,
          Script = VarInt.GetBytes(data.Length).Concat(data).ToArray()
        };

        SendTX(tXOutput, feePerByte);
      }

      const int LENGTH_TX_P2PKH = 120;

      void SendTX(TXOutputBToken tXOutput, double feePerByte)
      {
        long fee = (long)(feePerByte * LENGTH_TX_P2PKH);

        if (AccountWalletUnconfirmed.Balance < tXOutput.Value + fee)
          throw new ProtocolException(
            $"Not enough funds: balance {AccountWalletUnconfirmed.Balance} " +
            $"less than tX output value {tXOutput.Value} plus fee {fee} totaling {tXOutput.Value + fee}.");

        TXBToken tX = new()
        {
          KeyPublic = KeyPublic,
          BlockheightAccountCreated = AccountWalletUnconfirmed.BlockHeightAccountCreated,
          Nonce = AccountWalletUnconfirmed.Nonce,
          Fee = fee
        };

        tX.TXOutputs.Add(tXOutput);

        tX.Serialize(this);

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