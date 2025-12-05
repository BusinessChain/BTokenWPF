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

      int SerialNumberTX;

      LiteDatabase Database;
      ILiteCollection<DBRecordTXWallet> DatabaseTXCollection;

      static int LENGTH_P2PKH_TX = 120;


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
        (byte)TokenBToken.TypesToken.Coinbase
      };

        tXRaw.AddRange(BitConverter.GetBytes(blockHeight));

        tXRaw.Add(0x01); // count outputs

        tXRaw.AddRange(BitConverter.GetBytes(blockReward));
        tXRaw.AddRange(PublicKeyHash160);

        return Token.ParseTX(tXRaw.ToArray(), SHA256);
      }

      public override bool TrySendTXValue(string addressOutput, long valueOutput, double feePerByte)
      {
        if (!Token.TryCopyAccountUnconfirmed(PublicKeyHash160, out Account accountUnconfirmed))
          return false;

        // apply the Wallet's saved unconfirmed tXs on accountWalletUnconfirmed.

        return Token.TrySendTXValue(addressOutput, valueOutput, feePerByte, accountUnconfirmed);
      }

      public override bool TrySendTXData(byte[] data, double feePerByte)
      {
        if (!Token.TryCopyAccountUnconfirmed(PublicKeyHash160, out Account accountUnconfirmed))
          return false;

        // apply the Wallet's saved unconfirmed tXs on accountWalletUnconfirmed.

        return Token.TrySendTXData(data, feePerByte, accountUnconfirmed);
      }

      public override void InsertTX(TX tX, int heightBlock)
      {
        TXBToken tXBToken = tX as TXBToken;

        bool flagIndexTX = false;

        if (tXBToken.IDAccountSource.IsAllBytesEqual(PublicKeyHash160))
          flagIndexTX = true;

        foreach (TXOutputBToken output in tXBToken.TXOutputs)
          if (output.IDAccount.IsAllBytesEqual(PublicKeyHash160))
            flagIndexTX = true;

        if(flagIndexTX)
        {
          DBRecordTXWallet record = new ()
          {
            HashTX = tX.Hash,
            BlockHeightOriginTX = heightBlock,
            SerialNumberTX = SerialNumberTX++,
            TXRaw = tX.TXRaw
          };
          DatabaseTXCollection.Upsert(record);
        }
      }

      public override void ReverseBlock(Block block)
      {

      }

      public override long GetBalance()
      {
        try
        {
          Account accountUnconfirmed = Token.GetAccountUnconfirmed(PublicKeyHash160);
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