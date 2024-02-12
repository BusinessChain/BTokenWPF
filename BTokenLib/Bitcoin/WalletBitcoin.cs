using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace BTokenLib
{
  public partial class WalletBitcoin : Wallet
  {
    const int LENGTH_DATA_TX_SCAFFOLD = 10;
    const int LENGTH_DATA_P2PKH_OUTPUT = 34;
    const int LENGTH_DATA_P2PKH_INPUT = 180;

    public List<TXOutputWallet> OutputsSpendable = new();


    public WalletBitcoin(string privKeyDec, Token token)
      : base(privKeyDec, token)
    { }

    public override TX CreateTX(string address, long value, long fee)
    {
      byte[] pubKeyHash160 = Base58CheckToPubKeyHash(address);

      byte[] pubScript = PREFIX_P2PKH
        .Concat(pubKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();

      List<byte> tXRaw = new();
      long feeTX = 0;

      List<TXOutputWallet> inputs = GetOutputsSpendable();

      //List<TXOutputWallet> inputs = new()
      //{
      //  new TXOutputWallet()
      //  {
      //    TXID = "bcb81f7e7d843bcace5a794c0f36ed5abd4323087cabf9691db0aa36e4c9366b".ToBinary(),
      //    Value = 82000,
      //    Index = 0
      //  }
      //};

      long valueChange = inputs.Sum(i => i.Value) - value - fee;

      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
      tXRaw.AddRange(VarInt.GetBytes(inputs.Count));

      int indexFirstInput = tXRaw.Count;

      for (int i = 0; i < inputs.Count; i += 1)
      {
        tXRaw.AddRange(inputs[i].TXID);
        tXRaw.AddRange(BitConverter.GetBytes(inputs[i].Index));
        tXRaw.Add(0x00); // length empty script
        tXRaw.AddRange(BitConverter.GetBytes((int)0)); // sequence

        feeTX += inputs[i].Value;
      }

      tXRaw.Add((byte)(valueChange > 0 ? 2 : 1));

      tXRaw.AddRange(BitConverter.GetBytes(value));
      tXRaw.Add((byte)pubScript.Length);
      tXRaw.AddRange(pubScript);

      if (valueChange > 0)
      {
        tXRaw.AddRange(BitConverter.GetBytes(valueChange));
        tXRaw.Add((byte)PublicScript.Length);
        tXRaw.AddRange(PublicScript);

        feeTX -= valueChange;
      }

      tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      List<List<byte>> signaturesPerInput = new();

      for (int i = 0; i < inputs.Count; i += 1)
      {
        List<byte> tXRawSign = tXRaw.ToList();
        int indexRawSign = indexFirstInput + 36 * (i + 1) + 5 * i;

        tXRawSign[indexRawSign++] = (byte)PublicScript.Length;
        tXRawSign.InsertRange(indexRawSign, PublicScript);

        signaturesPerInput.Add(GetScriptSignature(tXRawSign.ToArray()));
      }

      for (int i = inputs.Count - 1; i >= 0; i -= 1)
      {
        int indexSign = indexFirstInput + 36 * (i + 1) + 5 * i;

        tXRaw[indexSign++] = (byte)signaturesPerInput[i].Count;

        tXRaw.InsertRange(
          indexSign,
          signaturesPerInput[i]);
      }

      tXRaw.RemoveRange(tXRaw.Count - 4, 4);

      int index = 0;

      TX tX = Token.ParseTX(
        tXRaw.ToArray(),
        ref index,
        SHA256);

      if (valueChange > 0)
        AddOutputUnconfirmed(
          new TXOutputWallet
          {
            TXID = tX.Hash,
            Index = 1,
            Value = valueChange
          });

      tX.TXRaw = tXRaw;

      tX.Fee = feeTX;

      return tX;
    }

    public override bool CreateTXData(byte[] data, long fee, out TX tX)
    {
      TXBitcoin tXBitcoin = new();
      tX = tXBitcoin;

      long feeAccrued = (long)(Token.FeeSatoshiPerByte * LENGTH_DATA_TX_SCAFFOLD);
      long feeData = (long)(Token.FeeSatoshiPerByte * data.Length);
      long feeInput = (long)Token.FeeSatoshiPerByte * LENGTH_DATA_P2PKH_INPUT;
      long feeOutputChange = (long)(Token.FeeSatoshiPerByte * LENGTH_DATA_P2PKH_OUTPUT);

      long valueAccrued = 0;

      List<TXOutputWallet> outputsSpendable = GetOutputsSpendable();

      long valueChange = outputsSpendable.Sum(i => i.Value) - fee;

      feeAccrued += feeInput * outputsSpendable.Count;
      feeAccrued += feeData;

      foreach (TXOutputWallet tXOutputWallet in outputsSpendable)
      {
        TXInput tXInput = new()
        {
          TXIDOutput = tXOutputWallet.TXID,
          OutputIndex = tXOutputWallet.Index
        };

        tXBitcoin.Inputs.Add(tXInput);
        valueAccrued += tXOutputWallet.Value;
      }

      if (valueAccrued < feeAccrued)
      {
        return false; // die outputs müssen wieder freigegeben werden!!
      }

      tokenAnchor.ValueChange = valueAccrued - feeAccrued - feeOutputChange;

      tokenAnchor.Serialize(this, SHA256, data);

      if (tokenAnchor.ValueChange > 0)
        AddOutputUnconfirmed(
          new TXOutputWallet
          {
            TXID = tokenAnchor.TX.Hash,
            Index = 1,
            Value = tokenAnchor.ValueChange
          });

      return true;
    }

    List<TXOutputWallet> GetOutputsSpendable()
    {
      long feePerTXOutput = (long)Token.FeeSatoshiPerByte * LENGTH_DATA_P2PKH_INPUT;

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