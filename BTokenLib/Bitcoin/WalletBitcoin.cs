using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace BTokenLib
{
  public partial class WalletBitcoin : Wallet
  {
    public byte[] PublicScript;

    const int LENGTH_P2PKH_OUTPUT = 34;
    const int LENGTH_P2PKH_INPUT = 180;

    byte OP_RETURN = 0x6A;

    public List<TXOutputWallet> OutputsSpendable = new();


    public WalletBitcoin(string privKeyDec, Token token)
      : base(privKeyDec, token)
    {
      PublicScript = PREFIX_P2PKH
        .Concat(PublicKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();
    }

    public override bool TryCreateTX(
      string addressOutput, 
      long valueOutput, 
      double feePerByte, 
      out TX tX)
    {
      tX = new TXBitcoin();

      if (!TryCreateTXInputScaffold(
        sequence : 0,
        valueNettoMinimum: (long)(LENGTH_P2PKH_OUTPUT * feePerByte),
        feePerByte,
        out long valueInput,
        out long feeTXInputScaffold,
        ref tX.TXRaw))
      {
        return false;
      }

      long feeTX = feeTXInputScaffold
        + (long)(LENGTH_P2PKH_OUTPUT * feePerByte)
        + (long)(LENGTH_P2PKH_OUTPUT * feePerByte);

      if (valueInput - feeTX > 0)
      {
        tX.TXRaw.Add(0x02);

        tX.TXRaw.AddRange(BitConverter.GetBytes(valueInput - feeTX));
        tX.TXRaw.Add((byte)PublicScript.Length);
        tX.TXRaw.AddRange(PublicScript);

        AddOutputUnconfirmed(
          new TXOutputWallet
          {
            TXID = tX.Hash,
            Index = 1,
            Value = valueInput - feeTX
          });

        tX.Fee = feeTX;
      }
      else
      {
        tX.TXRaw.Add(0x01);
        tX.Fee = valueInput - valueOutput;
      }

      //List<TXOutputWallet> inputs = new()
      //{
      //  new TXOutputWallet()
      //  {
      //    TXID = "bcb81f7e7d843bcace5a794c0f36ed5abd4323087cabf9691db0aa36e4c9366b".ToBinary(),
      //    Value = 82000,
      //    Index = 0
      //  }
      //};

      byte[] pubKeyHash160 = Base58CheckToPubKeyHash(addressOutput);

      byte[] pubScript = PREFIX_P2PKH
        .Concat(pubKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();

      tX.TXRaw.AddRange(BitConverter.GetBytes(valueOutput));
      tX.TXRaw.Add((byte)pubScript.Length);
      tX.TXRaw.AddRange(pubScript);

      tX.TXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tX.TXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      SignRTX(tX);

      tX.Hash = SHA256.ComputeHash(
       SHA256.ComputeHash(tX.TXRaw.ToArray()));

      return true;
    }

    public override bool TryCreateTXData(byte[] data, int sequence, out TX tX)
    {
      tX = new TXBitcoin();

      if (!TryCreateTXInputScaffold(
        sequence,
        (long)(data.Length * Token.FeeSatoshiPerByte),
        Token.FeeSatoshiPerByte,
        out long valueInput,
        out long feeTXInputScaffold,
        ref tX.TXRaw))
      {
        return false;
      }

      long feeTX = feeTXInputScaffold
        + (long)(LENGTH_P2PKH_OUTPUT * Token.FeeSatoshiPerByte)
        + (long)(data.Length * Token.FeeSatoshiPerByte);

      if(valueInput - feeTX > 0)
      {
        tX.TXRaw.Add(0x02);

        tX.TXRaw.AddRange(BitConverter.GetBytes(valueInput - feeTX));
        tX.TXRaw.Add((byte)PublicScript.Length);
        tX.TXRaw.AddRange(PublicScript);

        AddOutputUnconfirmed(
          new TXOutputWallet
          {
            TXID = tX.Hash,
            Index = 1,
            Value = valueInput - feeTX
          }); 
        
        tX.Fee = feeTX;
      }
      else
      {
        tX.TXRaw.Add(0x01);
        tX.Fee = valueInput;
      }

      tX.TXRaw.AddRange(BitConverter.GetBytes((long)0));
      tX.TXRaw.AddRange(VarInt.GetBytes(data.Length + 2));
      tX.TXRaw.Add(OP_RETURN);
      tX.TXRaw.Add((byte)data.Length);
      tX.TXRaw.AddRange(data);

      tX.TXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tX.TXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      SignRTX(tX);

      tX.Hash = SHA256.ComputeHash(
       SHA256.ComputeHash(tX.TXRaw.ToArray()));

      return true;
    }

    void SignRTX(TX tX)
    {
      List<List<byte>> signaturesPerInput = new();
      int countInputs = tX.TXRaw[4];
      int indexFirstInput = 5;

      for (int i = 0; i < countInputs; i += 1)
      {
        List<byte> tXRawSign = tX.TXRaw.ToList();
        int indexRawSign = indexFirstInput + 36 * (i + 1) + 5 * i;

        tXRawSign[indexRawSign++] = (byte)PublicScript.Length;
        tXRawSign.InsertRange(indexRawSign, PublicScript);

        signaturesPerInput.Add(
          GetScriptSignature(tXRawSign.ToArray()));
      }

      for (int i = countInputs - 1; i >= 0; i -= 1)
      {
        int indexSig = indexFirstInput + 36 * (i + 1) + 5 * i;

        tX.TXRaw[indexSig++] = (byte)signaturesPerInput[i].Count;

        tX.TXRaw.InsertRange(
          indexSig,
          signaturesPerInput[i]);
      }

      tX.TXRaw.RemoveRange(tX.TXRaw.Count - 4, 4);
    }

    bool TryCreateTXInputScaffold(
      int sequence,
      long valueNettoMinimum,
      double feePerByte,
      out long value, 
      out long feeTXInputScaffold,
      ref List<byte> tXInputScaffold)
    {
      tXInputScaffold = new();
      long feePerTXInput = (long)(feePerByte * LENGTH_P2PKH_INPUT);

      List<TXOutputWallet> outputsSpendable =
        OutputsSpendable.Where(o => o.Value > feePerTXInput)
        .Concat(OutputsUnconfirmed.Where(o => o.Value > feePerTXInput))
        .Except(OutputsUnconfirmedSpent)
        .Take(VarInt.PREFIX_UINT16 - 1).ToList();

      value = outputsSpendable.Sum(o => o.Value);
      feeTXInputScaffold = feePerTXInput * outputsSpendable.Count;

      if (value - feeTXInputScaffold < valueNettoMinimum)
        return false;

      OutputsUnconfirmedSpent.AddRange(outputsSpendable);

      tXInputScaffold.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
      tXInputScaffold.Add((byte)outputsSpendable.Count);

      foreach (TXOutputWallet tXOutputWallet in outputsSpendable)
      {
        tXInputScaffold.AddRange(tXOutputWallet.TXID);
        tXInputScaffold.AddRange(BitConverter.GetBytes(tXOutputWallet.Index));
        tXInputScaffold.Add(0x00); // length empty script
        tXInputScaffold.AddRange(BitConverter.GetBytes(sequence));
      }

      return true;
    }

    List<TXOutputWallet> GetOutputsSpendable()
    {
      long feePerTXOutput = (long)Token.FeeSatoshiPerByte * LENGTH_P2PKH_INPUT;

      List<TXOutputWallet> outputsValueNotSpendable = new();

      outputsValueNotSpendable.AddRange(
        OutputsSpendable.Where(o => o.Value > feePerTXOutput)
        .Concat(OutputsUnconfirmed.Where(o => o.Value > feePerTXOutput))
        .Except(OutputsUnconfirmedSpent)
        .Take(VarInt.PREFIX_UINT16 - 1));

      OutputsUnconfirmedSpent.AddRange(outputsValueNotSpendable);

      outputsValueNotSpendable.ForEach(o => BalanceUnconfirmed -= o.Value);

      return outputsValueNotSpendable;
    }

    public override void LoadImage(string path)
    {
      base.LoadImage(path);

      LoadOutputs(OutputsSpendable, Path.Combine(path, "OutputsValue"));
    }

    public override void CreateImage(string path)
    {
      base.CreateImage(path);

      StoreOutputs(OutputsSpendable, Path.Combine(path, "OutputsValue"));
    }

    public override void InsertBlock(Block block)
    {
      foreach (TXBitcoin tX in block.TXs)
        foreach (TXOutputBitcoin tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
            $"AddOutput to wallet {Token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, Token.LogFile, Token.LogEntryNotifier);

            TXOutputWallet outputValueUnconfirmed = OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tX.Hash));
            if (outputValueUnconfirmed != null)
            {
              BalanceUnconfirmed -= outputValueUnconfirmed.Value;
              OutputsUnconfirmed.Remove(outputValueUnconfirmed);
            }

            OutputsSpendable.Add(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                Index = tX.TXOutputs.IndexOf(tXOutput),
                Value = tXOutput.Value
              });

            AddTXToHistory(tX);

            Balance += tXOutput.Value;

            $"Balance of wallet {Token}: {Balance}".Log(this, Token.LogFile, Token.LogEntryNotifier);
          }

      foreach (TXBitcoin tX in block.TXs)
        foreach (TXInput tXInput in tX.Inputs)
        {
          $"Try spend input in wallet {Token} refing output: {tXInput.TXIDOutput.ToHexString()}, index {tXInput.OutputIndex}".Log(this, Token.LogFile, Token.LogEntryNotifier);

          TXOutputWallet outputValueUnconfirmedSpent = OutputsUnconfirmedSpent
            .Find(o => o.TXID.IsEqual(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

          if (outputValueUnconfirmedSpent != null)
          {
            OutputsUnconfirmedSpent.Remove(outputValueUnconfirmedSpent);
            BalanceUnconfirmed += outputValueUnconfirmedSpent.Value;
          }

          TXOutputWallet tXOutputWallet = OutputsSpendable.Find(o =>
            o.TXID.IsEqual(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

          if (tXOutputWallet != null)
          {
            Balance -= tXOutputWallet.Value;
            OutputsSpendable.Remove(tXOutputWallet);
            AddTXToHistory(tX);

            $"Balance of wallet {Token}: {Balance}".Log(this, Token.LogFile, Token.LogEntryNotifier);
          }
        }
    }

    public override void ReverseTXUnconfirmed(TX tX)
    {
      TXBitcoin tXBitcoin = (TXBitcoin)tX;

      TXOutputWallet outputValueUnconfirmed = 
        OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tX.Hash));

      if (outputValueUnconfirmed != null)
      {
        OutputsUnconfirmed.Remove(outputValueUnconfirmed);
        BalanceUnconfirmed -= outputValueUnconfirmed.Value;
      }

      foreach(TXInput tXInput in tXBitcoin.Inputs)
      {
        TXOutputWallet outputValueUnconfirmedSpent = OutputsUnconfirmedSpent
          .Find(o => o.TXID.IsEqual(tXInput.TXIDOutput));

        if (outputValueUnconfirmedSpent != null)
        {
          OutputsUnconfirmedSpent.Remove(outputValueUnconfirmedSpent);
          BalanceUnconfirmed += outputValueUnconfirmedSpent.Value;
        }
      }
    }
        
    public override void Clear()
    {
      OutputsSpendable.Clear();
      base.Clear();
    }
  }
}