using System;
using System.Collections.Generic;
using System.Linq;


namespace BTokenLib
{
  public partial class WalletBToken : Wallet
  {
    public int NonceAccount;


    public WalletBToken(string privKeyDec)
      : base(privKeyDec)
    { }

    public override TX CreateTX(string address, long value, long fee)
    {
      byte[] pubKeyHash160 = Base58CheckToPubKeyHash(address);

      byte[] pubScript = PREFIX_P2PKH
        .Concat(pubKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();

      List<byte> tXRaw = new();

      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
      tXRaw.Add(0x01); // number of inputs

      tXRaw.AddRange(PublicKeyHash160.Concat(new byte[12])); // input TXID
      tXRaw.AddRange(BitConverter.GetBytes(NonceAccount));
      tXRaw.Add(0x00); // length empty script
      tXRaw.AddRange(BitConverter.GetBytes((int)0)); // sequence

      tXRaw.Add(0x01); // number of outputs

      tXRaw.AddRange(BitConverter.GetBytes(value));
      tXRaw.Add((byte)pubScript.Length);
      tXRaw.AddRange(pubScript);

      tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      List<byte> tXRawSign = tXRaw.ToList();
      int indexSign = 41;

      tXRawSign[indexSign++] = (byte)PublicScript.Length;
      tXRawSign.InsertRange(indexSign, PublicScript);

      List<byte> signaturePerInput = GetScriptSignature(tXRawSign.ToArray());

      indexSign = 41;

      tXRaw[indexSign++] = (byte)signaturePerInput.Count;
      tXRaw.InsertRange(indexSign, signaturePerInput);

      tXRaw.RemoveRange(tXRaw.Count - 4, 4);

      int index = 0;

      TX tX = Block.ParseTX(
        tXRaw.ToArray(),
        ref index,
        SHA256);

      tX.TXRaw = tXRaw;

      tX.Fee = fee;

      return tX;
    }

    public override void InsertBlock(Block block, Token token)
    {
      foreach (TX tX in block.TXs)
        foreach (TXOutput tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
            $"AddOutput to wallet {token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, token.LogFile, token.LogEntryNotifier);

            TXOutputWallet outputValueUnconfirmed = OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tX.Hash));
            if (outputValueUnconfirmed != null)
            {
              BalanceUnconfirmed -= outputValueUnconfirmed.Value;
              OutputsUnconfirmed.Remove(outputValueUnconfirmed);
            }

            AddTXToHistory(tX);

            Balance += tXOutput.Value;

            $"Balance of wallet {token}: {Balance}".Log(this, token.LogFile, token.LogEntryNotifier);
          }

      foreach (TX tX in block.TXs)
        foreach (TXInput tXInput in tX.TXInputs)
        {
          $"Try spend input in wallet {token} refing output: {tXInput.TXIDOutput.ToHexString()}, index {tXInput.OutputIndex}".Log(this, token.LogFile, token.LogEntryNotifier);

          TXOutputWallet outputValueUnconfirmedSpent = OutputsUnconfirmedSpent
            .Find(o => o.TXID.IsEqual(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

          if (outputValueUnconfirmedSpent != null)
          {
            OutputsUnconfirmedSpent.Remove(outputValueUnconfirmedSpent);
            BalanceUnconfirmed += outputValueUnconfirmedSpent.Value;
          }

          if (tXInput.TXIDOutput.IsEqual(PublicKeyHash160) && tXInput.OutputIndex == NonceAccount)
          {
            Balance -= tX.TXOutputs.Sum(o => o.Value);
            AddTXToHistory(tX);
            $"Balance of wallet {token}: {Balance}".Log(this, token.LogFile, token.LogEntryNotifier);
          }
        }
    }


    public override bool CreateDataTX(
      double feeSatoshiPerByte,
      byte[] data,
      out Token.TokenAnchor tokenAnchor)
    {
      throw new NotImplementedException();
    }

    public override void ReverseTXUnconfirmed(TX tX)
    {
    }
  }
}