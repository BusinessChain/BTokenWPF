using System;
using System.Collections.Generic;

namespace BTokenLib
{
  public partial class WalletBToken : Wallet
  {
    public TokenBToken Token;

    static int LENGTH_P2PKH_TX = 120;


    public WalletBToken(string privKeyDec, TokenBToken token)
      : base(privKeyDec)
    {
      Token = token;
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

    public override bool TryCreateTX(
      string addressOutput,
      long valueOutput,
      double feePerByte,
      out TX tX)
    {
      tX = null;

      long fee = (long)(feePerByte * LENGTH_P2PKH_TX);

      Account accountUnconfirmed = Token.GetAccountUnconfirmed(PublicKeyHash160);

      if (accountUnconfirmed.Value < valueOutput + fee)
        throw new ProtocolException($"Account {PublicKeyHash160} does not contain enough funds {accountUnconfirmed.Value}.");

      List<byte> tXRaw = new();

      tXRaw.Add((byte)TokenBToken.TypesToken.ValueTransfer);

      tXRaw.AddRange(PublicKey);
      tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.BlockHeightAccountInit));
      tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.Nonce));

      tXRaw.AddRange(BitConverter.GetBytes(fee));

      tXRaw.Add(0x01); // count outputs

      tXRaw.AddRange(BitConverter.GetBytes(valueOutput));
      tXRaw.AddRange(Base58CheckToPubKeyHash(addressOutput));

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

      if (accountUnconfirmed.Value < fee)
        throw new ProtocolException($"Account {PublicKeyHash160} does not contain enough funds {accountUnconfirmed.Value}.");

      List<byte> tXRaw = new();

      tXRaw.Add((byte)TokenBToken.TypesToken.Data);

      tXRaw.AddRange(PublicKey);
      tXRaw.AddRange(BitConverter.GetBytes(accountUnconfirmed.BlockHeightAccountInit));
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

    public override void InsertTXUnconfirmed(TX tX)
    {

    }

    public override void LoadImage(string path)
    {
      throw new NotImplementedException();
    }

    public override long GetBalance()
    {
      try
      {
        Account accountUnconfirmed = Token.GetAccountUnconfirmed(PublicKeyHash160);
        return accountUnconfirmed.Value;
      }
      catch
      {
        return 0;
      }
    }
  }
}