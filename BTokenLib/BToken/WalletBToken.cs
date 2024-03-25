using System;
using System.Collections.Generic;
using System.Linq;


namespace BTokenLib
{
  public partial class WalletBToken : Wallet
  {
    public enum TypesToken
    {
      Coinbase = 0,
      ValueTransfer = 1,
      AnchorToken = 2,
      Data = 3
    }

    static int LENGTH_P2PKH_TX = 120;
    public long NonceAccount;


    public WalletBToken(string privKeyDec, Token token)
      : base(privKeyDec, token)
    { }


    public override TX CreateCoinbaseTX(int height, long blockReward)
    {
      TXBTokenCoinbase tX = new();

      tX.TXRaw.AddRange(BitConverter.GetBytes((int)TypesToken.Coinbase)); // token ; config

      tX.TXRaw.Add(0x01); // count outputs

      tX.TXRaw.AddRange(BitConverter.GetBytes(blockReward));
      tX.TXRaw.AddRange(PublicKeyHash160);

      byte[] signature = Crypto.GetSignature(
        PrivKeyDec,
        tX.TXRaw.ToArray(),
        SHA256);

      tX.TXRaw.Add((byte)(signature.Length + 1));
      tX.TXRaw.AddRange(signature);

      tX.Hash = SHA256.ComputeHash(
       SHA256.ComputeHash(tX.TXRaw.ToArray()));

      return tX;
    }

    public override bool TryCreateTX(
      string addressOutput,
      long valueOutput,
      double feePerByte,
      out TX tX)
    {
      tX = new TXBTokenValueTransfer();

      tX.Fee = (long)(feePerByte * LENGTH_P2PKH_TX);

      if (BalanceUnconfirmed < valueOutput + tX.Fee)
        return false;

      tX.TXRaw.AddRange(BitConverter.GetBytes((int)TypesToken.ValueTransfer)); // token ; config

      tX.TXRaw.AddRange(PublicKey);
      tX.TXRaw.AddRange(BitConverter.GetBytes(NonceAccount));
      tX.TXRaw.AddRange(BitConverter.GetBytes(tX.Fee));

      tX.TXRaw.Add(0x01); // count outputs

      tX.TXRaw.AddRange(BitConverter.GetBytes(valueOutput));
      tX.TXRaw.AddRange(Base58CheckToPubKeyHash(addressOutput));
      
      byte[] signature = Crypto.GetSignature(
        PrivKeyDec,
        tX.TXRaw.ToArray(),
        SHA256);

      tX.TXRaw.Add((byte)(signature.Length + 1));
      tX.TXRaw.AddRange(signature);

      tX.Hash = SHA256.ComputeHash(
       SHA256.ComputeHash(tX.TXRaw.ToArray()));

      return true;
    }

    public override bool TryCreateTXData(byte[] data, int sequence, out TX tX)
    {
      tX = new TXBTokenData();

      tX.Fee = (long)(Token.FeeSatoshiPerByte * LENGTH_P2PKH_TX);

      if (BalanceUnconfirmed < tX.Fee)
        return false;

      tX.TXRaw.AddRange(BitConverter.GetBytes((int)TypesToken.Data)); // token ; config

      tX.TXRaw.AddRange(PublicKey);
      tX.TXRaw.AddRange(BitConverter.GetBytes(NonceAccount));
      tX.TXRaw.AddRange(BitConverter.GetBytes(tX.Fee));

      tX.TXRaw.Add(0x01);
      tX.TXRaw.AddRange(VarInt.GetBytes(data.Length));
      tX.TXRaw.AddRange(data);

      byte[] signature = Crypto.GetSignature(
        PrivKeyDec,
        tX.TXRaw.ToArray(),
        SHA256);

      tX.TXRaw.Add((byte)(signature.Length + 1));
      tX.TXRaw.AddRange(signature);

      tX.Hash = SHA256.ComputeHash(
       SHA256.ComputeHash(tX.TXRaw.ToArray()));

      return true;
    }

    public void InsertTXBTokenCoinbase(TXBTokenCoinbase tX)
    {
      foreach (TXOutputBToken tXOutput in tX.TXOutputs)
      {
        if (!tXOutput.IDAccount.IsEqual(PublicKeyHash160))
          continue;

        $"AddOutput to wallet {Token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, Token.LogFile, Token.LogEntryNotifier);
          
        AddTXToHistory(tX);

        Balance += tXOutput.Value;
      }
    }

    public void InsertTXBTokenValueTransfer(TXBTokenValueTransfer tX)
    {
      if (tX.IDAccountSource.IsEqual(PublicKeyHash160))
      {
        $"Try spend from {Token} wallet: {tX.IDAccountSource.ToHexString()} nonce: {tX.Nonce}.".Log(this, Token.LogFile, Token.LogEntryNotifier);

        Balance -= tX.TXOutputs.Sum(o => o.Value);
        AddTXToHistory(tX);
      }

      foreach (TXOutputBToken tXOutput in tX.TXOutputs)
      {
        if (!tXOutput.IDAccount.IsEqual(PublicKeyHash160))
          continue;

        $"AddOutput to wallet {Token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, Token.LogFile, Token.LogEntryNotifier);

        TXOutputWallet outputValueUnconfirmed =
          OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tX.Hash));

        if (outputValueUnconfirmed != null)
        {
          BalanceUnconfirmed -= outputValueUnconfirmed.Value;
          OutputsUnconfirmed.Remove(outputValueUnconfirmed);
        }

        AddTXToHistory(tX);

        Balance += tXOutput.Value;
      }

      $"Balance of wallet {Token}: {Balance}".Log(this, Token.LogFile, Token.LogEntryNotifier);
    }

    public override void ReverseTXUnconfirmed(TX tX)
    {
    }
  }
}