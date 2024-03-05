using System;
using System.Collections.Generic;
using System.Linq;


namespace BTokenLib
{
  public partial class WalletBToken : Wallet
  {
    const int LENGTH_P2PKH_TX = 120;
    public long NonceAccount;


    public WalletBToken(string privKeyDec, Token token)
      : base(privKeyDec, token)
    { }

    public override bool TryCreateTX(
      string addressOutput,
      long valueOutput,
      double feePerByte,
      out TX tX)
    {
      tX = new TXBToken();

      tX.Fee = (long)(feePerByte * LENGTH_P2PKH_TX);

      if (BalanceUnconfirmed < valueOutput + tX.Fee)
        return false;

      tX.TXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // token ; config

      tX.TXRaw.AddRange(PublicKey);
      tX.TXRaw.AddRange(BitConverter.GetBytes(NonceAccount));
      tX.TXRaw.AddRange(BitConverter.GetBytes(tX.Fee));

      tX.TXRaw.Add(0x01); // count outputs

      tX.TXRaw.Add((byte)TXOutputBToken.TypesToken.ValueTransfer);
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
      tX = new TXBToken();

      tX.Fee = (long)(Token.FeeSatoshiPerByte * LENGTH_P2PKH_TX);

      if (BalanceUnconfirmed < tX.Fee)
        return false;

      tX.TXRaw.AddRange(new byte[] { 0x02, 0x00, 0x00, 0x00 }); // token ; config

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

    public override void InsertBlock(Block block)
    {
      foreach (TXBToken tX in block.TXs)
        foreach (TXOutputBToken tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
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

            $"Balance of wallet {Token}: {Balance}".Log(this, Token.LogFile, Token.LogEntryNotifier);
          }

      foreach (TXBToken tX in block.TXs)
      {
        $"Try spend from {Token} wallet: {tX.IDAccountSource.ToHexString()} nonce: {tX.Nonce}.".Log(this, Token.LogFile, Token.LogEntryNotifier);

        if (tX.IDAccountSource.IsEqual(PublicKeyHash160) && tX.Nonce == NonceAccount)
        {
          Balance -= tX.TXOutputs.Sum(o => ((TXOutputBToken)o).Value);
          AddTXToHistory(tX);
          $"Balance of wallet {Token}: {Balance}".Log(this, Token.LogFile, Token.LogEntryNotifier);
        }
      }
    }

    public override void ReverseTXUnconfirmed(TX tX)
    {
    }
  }
}