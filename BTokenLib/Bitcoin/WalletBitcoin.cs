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

        tXRaw[indexSig++] = (byte)signaturesPerInput[i].Count;

        tXRaw.InsertRange(
          indexSig,
          signaturesPerInput[i]);
      }

      tXRaw.RemoveRange(tXRaw.Count - 4, 4);
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
        
    public void ReverseTXsUnconfirmed(List<TXBitcoin> tXs)
    {
      for(int i = tXs.Count - 1; i > -1; i -= 1)
      {
        TXOutputWallet outputValueUnconfirmed =
          OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tXs[i].Hash));

        if (outputValueUnconfirmed != null)
        {
          OutputsUnconfirmed.Remove(outputValueUnconfirmed);
          BalanceUnconfirmed -= outputValueUnconfirmed.Value;
        }

        foreach (TXInput tXInput in tXs[i].Inputs)
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
    }
        
    public override void Clear()
    {
      OutputsSpendable.Clear();
      base.Clear();
    }
  }
}