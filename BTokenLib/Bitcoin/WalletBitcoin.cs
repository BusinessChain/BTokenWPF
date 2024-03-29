﻿using System;
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

    public const byte LENGTH_SCRIPT_P2PKH = 25;
    public static byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    public static byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    public const byte LENGTH_SCRIPT_ANCHOR_TOKEN = 69;
    public const byte OP_RETURN = 0x6A;
    public static byte[] PREFIX_ANCHOR_TOKEN = 
      new byte[] { OP_RETURN }.Concat(Token.IDENTIFIER_BTOKEN_PROTOCOL).ToArray();

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

      long valueChange = valueInput - valueOutput - feeTX;

      if (valueChange > 0)
      {
        tX.TXRaw.Add(0x02);

        tX.TXRaw.AddRange(BitConverter.GetBytes(valueChange));
        tX.TXRaw.Add((byte)PublicScript.Length);
        tX.TXRaw.AddRange(PublicScript);

        AddOutputUnconfirmed(
          new TXOutputWallet
          {
            TXID = tX.Hash,
            Index = 1,
            Value = valueChange
          });

        tX.Fee = valueInput - valueOutput - valueChange;
      }
      else
      {
        tX.TXRaw.Add(0x01);
        tX.Fee = valueInput - valueOutput;
      }

      byte[] pubScript = PREFIX_P2PKH
        .Concat(Base58CheckToPubKeyHash(addressOutput))
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

    public override TX CreateCoinbaseTX(int height, long blockReward)
    {
      TXBitcoin tX = new();

      tX.TXRaw.AddRange(new byte[4] { 0x01, 0x00, 0x00, 0x00 }); // version

      tX.TXRaw.Add(0x01); // #TxIn

      tX.TXRaw.AddRange(new byte[32]); // TxOutHash

      tX.TXRaw.AddRange("FFFFFFFF".ToBinary()); // TxOutIndex

      List<byte> blockHeight = VarInt.GetBytes(height); // Script coinbase
      tX.TXRaw.Add((byte)blockHeight.Count);
      tX.TXRaw.AddRange(blockHeight);

      tX.TXRaw.AddRange("FFFFFFFF".ToBinary()); // sequence

      tX.TXRaw.Add(0x01); // #TxOut

      tX.TXRaw.AddRange(BitConverter.GetBytes(blockReward));

      tX.TXRaw.Add((byte)PublicScript.Length);
      tX.TXRaw.AddRange(PublicScript);

      tX.TXRaw.AddRange(new byte[4]);

      tX.Hash = SHA256.ComputeHash(
       SHA256.ComputeHash(tX.TXRaw.ToArray()));

      return tX;
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

        byte[] signature = Crypto.GetSignature(
        PrivKeyDec,
        tXRawSign.ToArray(),
        SHA256);

        List<byte> scriptSig = new();

        scriptSig.Add((byte)(signature.Length + 1));
        scriptSig.AddRange(signature);
        scriptSig.Add(0x01);

        scriptSig.Add((byte)PublicKey.Length);
        scriptSig.AddRange(PublicKey);

        signaturesPerInput.Add(scriptSig);
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

      List<TXOutputWallet> outputsSpendable = new()
      {
        new TXOutputWallet()
        {
          TXID = "058b32e3ac89a2a7b586820fc0755eba13fbce9e824d364420a0fd71e7f55ad5".ToBinary(),
          Value = 8056,
          Index = 0
        }
      };

      //OutputsSpendable.Where(o => o.Value > feePerTXInput)
      //.Concat(OutputsUnconfirmed.Where(o => o.Value > feePerTXInput))
      //.Except(OutputsUnconfirmedSpent)
      //.Take(VarInt.PREFIX_UINT16 - 1).ToList();
        

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

    public void InsertTX(TXBitcoin tX)
    {
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

      foreach (TXOutputBitcoin tXOutput in tX.TXOutputs)
      {
        if (tXOutput.Type == TXOutputBitcoin.TypesToken.ValueTransfer &&
          tXOutput.PublicKeyHash160.IsEqual(PublicKeyHash160))
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