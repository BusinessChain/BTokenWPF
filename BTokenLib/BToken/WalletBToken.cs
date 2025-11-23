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

      public List<TX> TXsUnconfirmed = new();

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

      public override bool TryCreateTX(string addressOutput, long valueOutput, double feePerByte, out TX tX)
      {
        tX = null;

        long fee = (long)(feePerByte * LENGTH_P2PKH_TX);

        Account accountUnconfirmed = Token.GetAccountUnconfirmed(PublicKeyHash160);

        if (accountUnconfirmed.Balance < valueOutput + fee)
          throw new ProtocolException($"Account {PublicKeyHash160} does not contain enough funds {accountUnconfirmed.Balance}.");

        List<byte> tXRaw = new();

        tXRaw.Add((byte)TypesToken.ValueTransfer);

        tXRaw.AddRange(PublicKey);
        tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.BlockHeightAccountCreated));
        tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.Nonce));

        tXRaw.AddRange(BitConverter.GetBytes(fee));

        tXRaw.Add(0x01); // count outputs

        tXRaw.AddRange(BitConverter.GetBytes(valueOutput));
        tXRaw.AddRange(addressOutput.Base58CheckToPubKeyHash());

        byte[] signature = Crypto.GetSignature(PrivKeyDec, tXRaw.ToArray());

        tXRaw.Add((byte)signature.Length);
        tXRaw.AddRange(signature);

        tX = Token.ParseTX(tXRaw.ToArray(), SHA256);

        return true;
      }

      public override bool TryCreateTXData(byte[] data, int sequence, double feePerByte, out TX tX)
      {
        tX = null;

        long fee = (long)(feePerByte * LENGTH_P2PKH_TX);

        Account accountUnconfirmed = Token.GetAccountUnconfirmed(PublicKeyHash160);

        if (accountUnconfirmed.Balance < fee)
          throw new ProtocolException($"Account {PublicKeyHash160} does not contain enough funds {accountUnconfirmed.Balance}.");

        List<byte> tXRaw = new();

        tXRaw.Add((byte)TokenBToken.TypesToken.Data);

        tXRaw.AddRange(PublicKey);
        tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.BlockHeightAccountCreated));
        tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.Nonce));

        tXRaw.AddRange(BitConverter.GetBytes(fee));

        tXRaw.Add(0x01);
        tXRaw.AddRange(VarInt.GetBytes(data.Length));
        tXRaw.AddRange(data);

        byte[] signature = Crypto.GetSignature(PrivKeyDec, tXRaw.ToArray());

        tXRaw.Add((byte)signature.Length);
        tXRaw.AddRange(signature);

        tX = Token.ParseTX(tXRaw.ToArray(), SHA256);

        return true;
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

      public override void InsertTXUnconfirmed(TX tX)
      {
        if (!TXsUnconfirmed.Any(t => t.Hash.IsAllBytesEqual(tX.Hash)))
          // Hier braucht es noch eine weitere Bedingung, nämlich dass die TX für uns von interesse ist.
          // Und falls nicht oder bei ungültigkeit, einfach returnen ohne exception.
          TXsUnconfirmed.Add(tX);
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