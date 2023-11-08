using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;


namespace BTokenLib
{
  public partial class WalletUTXOModel : Wallet
  {
    public List<TXOutputWallet> Outputs = new();


    public WalletUTXOModel(string privKeyDec)
      : base(privKeyDec)
    { }

    public TX CreateTX(string address, long value, long fee)
    {
      byte[] pubKeyHash160 = Base58CheckToPubKeyHash(address);

      byte[] pubScript = PREFIX_P2PKH
        .Concat(pubKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();

      List<byte> tXRaw = new();
      long feeTX = 0;

      List<TXOutputWallet> inputs = GetOutputs(value, out long feeOutputs);

      //List<TXOutputWallet> inputs = new()
      //{
      //  new TXOutputWallet()
      //  {
      //    TXID = "64f07568c00a215730b3323dc998be8d723d57a87e0a8ffd2a4c66081511f5e0".ToBinary(),
      //    Value = 87000,
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

      TX tX = Block.ParseTX(
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

    public override void LoadImage(string path)
    {
      base.LoadImage(path);

      LoadOutputs(Outputs, Path.Combine(path, "OutputsValue"));
    }

    public override void CreateImage(string path)
    {
      base.CreateImage(path);

      StoreOutputs(Outputs, Path.Combine(path, "OutputsValue"));
    }

    public void InsertBlock(Block block, Token token)
    {
      foreach (TX tX in block.TXs)
        foreach (TXOutput tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
            $"AddOutput to wallet {token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, token.LogFile, token.LogEntryNotifier);

            Outputs.Add(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                Index = tX.TXOutputs.IndexOf(tXOutput),
                Value = tXOutput.Value
              });

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

          TXOutputWallet tXOutputWallet = Outputs.Find(o =>
            o.TXID.IsEqual(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

          if (tXOutputWallet != null)
          {
            Outputs.Remove(tXOutputWallet);
            AddTXToHistory(tX);
            Balance -= tXOutputWallet.Value;

            $"Balance of wallet {token}: {Balance}".Log(this, token.LogFile, token.LogEntryNotifier);
          }
        }
    }


    bool TryDetectTXOutputSpendable(TXOutput tXOutput)
    {
      if (tXOutput.LengthScript != LENGTH_P2PKH)
        return false;

      int indexScript = tXOutput.StartIndexScript;

      if (!PREFIX_P2PKH.IsEqual(tXOutput.Buffer, indexScript))
        return false;

      indexScript += 3;

      if (!PublicKeyHash160.IsEqual(tXOutput.Buffer, indexScript))
        return false;

      indexScript += 20;

      if (!POSTFIX_P2PKH.IsEqual(tXOutput.Buffer, indexScript))
        return false;

      return true;
    }

    public void ReverseTXUnconfirmed(TX tX)
    {
      TXOutputWallet outputValueUnconfirmed = OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tX.Hash));
      if (outputValueUnconfirmed != null)
      {
        OutputsUnconfirmed.Remove(outputValueUnconfirmed);
        BalanceUnconfirmed -= outputValueUnconfirmed.Value;
      }

      foreach(TXInput tXInput in tX.TXInputs)
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

    public List<TXOutputWallet> GetOutputs(double feeSatoshiPerByte, out long feeOutputs)
    {
      long fee = (long)feeSatoshiPerByte * LENGTH_DATA_P2PKH_INPUT;

      List<TXOutputWallet> outputsValueNotSpent = new();

      outputsValueNotSpent.AddRange(
        Outputs.Where(o => o.Value > fee)
        .Concat(OutputsUnconfirmed.Where(o => o.Value > fee))
        .Except(OutputsUnconfirmedSpent)
        .Take(VarInt.PREFIX_UINT16 - 1));

      OutputsUnconfirmedSpent.AddRange(outputsValueNotSpent);

      outputsValueNotSpent.ForEach(o => BalanceUnconfirmed -= o.Value);

      feeOutputs = fee * outputsValueNotSpent.Count;
      return outputsValueNotSpent;
    }
    
    public void Clear()
    {
      Outputs.Clear();
      OutputsUnconfirmed.Clear();
      OutputsUnconfirmedSpent.Clear();

      Balance = 0;
      BalanceUnconfirmed = 0;
    }
  }
}