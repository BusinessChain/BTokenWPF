using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace BTokenLib
{
  public partial class WalletBToken : Wallet
  {
    public TokenBToken Token;

    static int LENGTH_P2PKH_TX = 120;
    public long NonceAccount;


    public WalletBToken(string privKeyDec, TokenBToken token)
      : base(privKeyDec)
    {
      Token = token;
    }


    public TXBToken CreateTXCoinbase(long blockReward)
    {
      List<byte> tXRaw = new();

      tXRaw.Add((byte)TokenBToken.TypesToken.Coinbase); // token ; config
      tXRaw.Add(0x01); // count outputs

      tXRaw.AddRange(BitConverter.GetBytes(blockReward));
      tXRaw.AddRange(PublicKeyHash160);

      return Token.ParseTX(tXRaw.ToArray(), SHA256, flagCoinbase: true);
    }

    public override bool TryCreateTX(
      string addressOutput,
      long valueOutput,
      double feePerByte,
      out TX tX)
    {
      long fee = (long)(feePerByte * LENGTH_P2PKH_TX);

      if (Balance < valueOutput + fee)
      {
        tX = null;
        return false;
      }

      List<byte> tXRaw = new();

      tXRaw.Add((byte)TokenBToken.TypesToken.ValueTransfer); // token ; config

      tXRaw.AddRange(PublicKey);
      tXRaw.AddRange(BitConverter.GetBytes(NonceAccount));
      tXRaw.AddRange(BitConverter.GetBytes(fee));

      tXRaw.Add(0x01); // count outputs

      tXRaw.AddRange(BitConverter.GetBytes(valueOutput));
      tXRaw.AddRange(Base58CheckToPubKeyHash(addressOutput));
      
      byte[] signature = Crypto.GetSignature(
        PrivKeyDec,
        tXRaw.ToArray(),
        SHA256);

      tXRaw.Add((byte)signature.Length);
      tXRaw.AddRange(signature);

      tX = Token.ParseTX(tXRaw.ToArray(), SHA256, flagCoinbase: false);

      return true;
    }

    public override bool TryCreateTXData(byte[] data, int sequence, double feePerByte, out TX tX)
    {
      List<byte> tXRaw = new();

      long fee = (long)(feePerByte * LENGTH_P2PKH_TX);

      if (Balance < fee)
      {
        tX = null;
        return false;
      }

      tXRaw.Add((byte)TokenBToken.TypesToken.Data); // token ; config

      tXRaw.AddRange(PublicKey);
      tXRaw.AddRange(BitConverter.GetBytes(NonceAccount));
      tXRaw.AddRange(BitConverter.GetBytes(fee));

      tXRaw.Add(0x01);
      tXRaw.AddRange(VarInt.GetBytes(data.Length));
      tXRaw.AddRange(data);

      byte[] signature = Crypto.GetSignature(
        PrivKeyDec,
        tXRaw.ToArray(),
        SHA256);

      tXRaw.Add((byte)signature.Length);
      tXRaw.AddRange(signature);

      tX = Token.ParseTX(tXRaw.ToArray(), SHA256, flagCoinbase: false);

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
          OutputsUnconfirmed.Remove(outputValueUnconfirmed);

        AddTXToHistory(tX);

        Balance += tXOutput.Value;
      }

      $"Balance of wallet {Token}: {Balance}".Log(this, Token.LogFile, Token.LogEntryNotifier);
    }
  }
}