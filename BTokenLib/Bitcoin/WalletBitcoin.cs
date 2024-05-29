using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace BTokenLib
{
  public partial class WalletBitcoin : Wallet
  {
    public TokenBitcoin Token;

    public byte[] PublicScript;

    const int LENGTH_P2PKH_OUTPUT = 34;
    const int LENGTH_P2PKH_INPUT = 180;

    public const byte LENGTH_SCRIPT_P2PKH = 25;
    public static byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    public static byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    public const byte OP_RETURN = 0x6A;
    public const byte LengthDataAnchorToken = 70;

    public static byte[] PREFIX_ANCHOR_TOKEN =
      new byte[] { OP_RETURN, LengthDataAnchorToken }.Concat(BTokenLib.Token.IDENTIFIER_BTOKEN_PROTOCOL).ToArray();

    public readonly static int LENGTH_SCRIPT_ANCHOR_TOKEN =
      PREFIX_ANCHOR_TOKEN.Length + TokenAnchor.LENGTH_IDTOKEN + 32 + 32;

    public List<TXOutputWallet> OutputsSpendable = new();

    public long Balance;


    public WalletBitcoin(string privKeyDec, TokenBitcoin token)
      : base(privKeyDec)
    {
      Token = token;

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
      if (!TryCreateTXInputScaffold(
        sequence : 0,
        valueNettoMinimum: (long)(LENGTH_P2PKH_OUTPUT * feePerByte),
        feePerByte,
        out long valueInput,
        out long feeTXInputScaffold,
        out List<byte> tXRaw))
      {
        tX = null;
        return false;
      }

      long feeTX = feeTXInputScaffold
        + (long)(LENGTH_P2PKH_OUTPUT * feePerByte)
        + (long)(LENGTH_P2PKH_OUTPUT * feePerByte);

      long valueChange = valueInput - valueOutput - feeTX;

      if (valueChange > 0)
      {
        tXRaw.Add(0x02);

        tXRaw.AddRange(BitConverter.GetBytes(valueChange));
        tXRaw.Add((byte)PublicScript.Length);
        tXRaw.AddRange(PublicScript);
      }
      else
        tXRaw.Add(0x01);

      byte[] pubScript = PREFIX_P2PKH
        .Concat(Base58CheckToPubKeyHash(addressOutput))
        .Concat(POSTFIX_P2PKH).ToArray();

      tXRaw.AddRange(BitConverter.GetBytes(valueOutput));
      tXRaw.Add((byte)pubScript.Length);
      tXRaw.AddRange(pubScript);

      tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      SignTX(tXRaw);

      MemoryStream stream = new(tXRaw.ToArray());

      tX = Token.ParseTX(stream, SHA256);
      return true;
    }

    public override bool TryCreateTXData(byte[] data, int sequence, double feePerByte, out TX tX)
    {
      if (!TryCreateTXInputScaffold(
        sequence,
        (long)(data.Length * feePerByte),
        feePerByte,
        out long valueInput,
        out long feeTXInputScaffold,
        out List<byte> tXRaw))
      {
        tX = null;
        return false;
      }

      long feeTX = feeTXInputScaffold
        + (long)(LENGTH_P2PKH_OUTPUT * feePerByte)
        + (long)(data.Length * feePerByte);

      long valueChange = valueInput - feeTX;

      if (valueChange > 0)
        tXRaw.Add(0x02);
      else
        tXRaw.Add(0x01);

      tXRaw.AddRange(BitConverter.GetBytes((long)0));
      tXRaw.Add((byte)(data.Length + 2));
      tXRaw.Add(OP_RETURN);
      tXRaw.Add((byte)data.Length);
      tXRaw.AddRange(data);

      if (valueChange > 0)
      {
        tXRaw.AddRange(BitConverter.GetBytes(valueChange));
        tXRaw.Add((byte)PublicScript.Length);
        tXRaw.AddRange(PublicScript);
      }

      tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      SignTX(tXRaw);

      MemoryStream stream = new(tXRaw.ToArray());

      tX = Token.ParseTX(stream, SHA256);
      return true;
    }

    void SignTX(List<byte> tXRaw)
    {
      List<List<byte>> signaturesPerInput = new();
      int countInputs = tXRaw[4];
      int indexFirstInput = 5;

      for (int i = 0; i < countInputs; i += 1)
      {
        List<byte> tXRawSign = tXRaw.ToList();
        int indexRawSign = indexFirstInput + 36 * (i + 1) + 5 * i;

        tXRawSign[indexRawSign++] = (byte)PublicScript.Length;
        tXRawSign.InsertRange(indexRawSign, PublicScript);

        byte[] message = SHA256.ComputeHash(tXRawSign.ToArray());

        byte[] signature = Crypto.GetSignature(PrivKeyDec, message);

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

        tXRaw[indexSig++] = (byte)signaturesPerInput[i].Count;

        tXRaw.InsertRange(
          indexSig,
          signaturesPerInput[i]);
      }

      tXRaw.RemoveRange(tXRaw.Count - 4, 4);
    }

    bool TryCreateTXInputScaffold(
      int sequence,
      long valueNettoMinimum,
      double feePerByte,
      out long value, 
      out long feeTXInputScaffold,
      out List<byte> tXRaw)
    {
      tXRaw = new();
      long feePerTXInput = (long)(feePerByte * LENGTH_P2PKH_INPUT);

      //List<TXOutputWallet> outputsSpendable = new()
      //{
      //  new TXOutputWallet()
      //  {
      //    TXID = "20da7491ec53757a914dc1f045afbcb0a5c3396785a9abe9fc074e017e9403fd".ToBinary(),
      //    Value = 7106,
      //    Index = 1
      //  }
      //};

      List<TXOutputWallet> outputsSpendable = OutputsSpendable
        .Where(o => o.Value > feePerTXInput)
        .Concat(OutputsUnconfirmed.Where(o => o.Value > feePerTXInput))
        .Except(OutputsUnconfirmedSpent)
        .Take(VarInt.PREFIX_UINT16 - 1).ToList();

      value = outputsSpendable.Sum(o => o.Value);
      feeTXInputScaffold = feePerTXInput * outputsSpendable.Count;

      if (value - feeTXInputScaffold < valueNettoMinimum || outputsSpendable.Count == 0)
        return false;

      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
      tXRaw.Add((byte)outputsSpendable.Count);

      foreach (TXOutputWallet tXOutputWallet in outputsSpendable)
      {
        tXRaw.AddRange(tXOutputWallet.TXID);
        tXRaw.AddRange(BitConverter.GetBytes(tXOutputWallet.Index));
        tXRaw.Add(0x00); // length empty script
        tXRaw.AddRange(BitConverter.GetBytes(sequence));
      }

      return true;
    }

    public override void LoadImage(string path)
    {
      base.LoadImage(path);

      using (FileStream fileStream = new(
        Path.Combine(path, "walletHistoryTransactions"),
        FileMode.Open,
        FileAccess.Read))
      {
        while (fileStream.Position < fileStream.Length)
          HistoryTransactions.Add(Token.ParseTX(fileStream, SHA256));
      }

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
          .Find(o => o.TXID.HasEqualElements(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

        if (outputValueUnconfirmedSpent != null)
          OutputsUnconfirmedSpent.Remove(outputValueUnconfirmedSpent);

        TXOutputWallet tXOutputWallet = OutputsSpendable.Find(o =>
          o.TXID.HasEqualElements(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

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
          tXOutput.PublicKeyHash160.HasEqualElements(PublicKeyHash160))
        {
          $"AddOutput to wallet {Token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, Token.LogFile, Token.LogEntryNotifier);

          TXOutputWallet outputValueUnconfirmed = OutputsUnconfirmed
            .Find(o => o.TXID.HasEqualElements(tX.Hash));
          
          if (outputValueUnconfirmed != null)
            OutputsUnconfirmed.Remove(outputValueUnconfirmed);

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

    public void InsertTXUnconfirmed(TXBitcoin tX)
    {
      foreach (TXInput tXInput in tX.Inputs)
      {
        ($"Try spend unconfirmed input in wallet {Token} refing output: {tXInput.TXIDOutput.ToHexString()}," +
          $"index {tXInput.OutputIndex}").Log(this, Token.LogFile, Token.LogEntryNotifier);

        TXOutputWallet outputSpendable = OutputsSpendable.Concat(OutputsUnconfirmed).ToList()
          .Find(o => o.TXID.HasEqualElements(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

        if(outputSpendable != null)
          OutputsUnconfirmedSpent.Add(outputSpendable);
      }

      foreach (TXOutputBitcoin tXOutput in tX.TXOutputs)
      {
        if (tXOutput.Type == TXOutputBitcoin.TypesToken.ValueTransfer &&
          tXOutput.PublicKeyHash160.HasEqualElements(PublicKeyHash160))
        {
          ($"AddOutput unconfirmes to wallet {Token}, TXID: {tX.Hash.ToHexString()}, " +
            $"Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}").Log(this, Token.LogFile, Token.LogEntryNotifier);

          OutputsUnconfirmed.Add(new TXOutputWallet
          {
            TXID = tX.Hash,
            Index = tX.TXOutputs.IndexOf(tXOutput),
            Value = tXOutput.Value
          });
        }
      }
    }

    public void ReverseTXsUnconfirmed(List<TXBitcoin> tXs)
    {
      for(int i = tXs.Count - 1; i > -1; i -= 1)
      {
        TXOutputWallet outputValueUnconfirmed =
          OutputsUnconfirmed.Find(o => o.TXID.HasEqualElements(tXs[i].Hash));

        if (outputValueUnconfirmed != null)
          OutputsUnconfirmed.Remove(outputValueUnconfirmed);

        foreach (TXInput tXInput in tXs[i].Inputs)
        {
          TXOutputWallet outputValueUnconfirmedSpent = OutputsUnconfirmedSpent
            .Find(o => o.TXID.HasEqualElements(tXInput.TXIDOutput));

          if (outputValueUnconfirmedSpent != null)
            OutputsUnconfirmedSpent.Remove(outputValueUnconfirmedSpent);
        }
      }
    }
        
    public override void Clear()
    {
      OutputsSpendable.Clear();
      Balance = 0;
      base.Clear();
    }

    public override long GetBalance()
    {
      return OutputsSpendable.Sum(o => o.Value);
    }
  }
}