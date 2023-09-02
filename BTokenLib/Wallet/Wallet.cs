using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using Org.BouncyCastle.Crypto.Digests;
using static BTokenLib.TokenBToken;


namespace BTokenLib
{
  public partial class Wallet
  {
    public enum TypeWallet
    {
      AccountType,
      UTXOType
    }

    TypeWallet Type;

    const int LENGTH_P2PKH = 25;
    byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    Crypto Crypto = new();

    SHA256 SHA256 = SHA256.Create();
    readonly RipeMD160Digest RIPEMD160 = new();

    string PrivKeyDec;
    byte[] PublicKey;
    byte[] PublicKeyHash160 = new byte[20];
    public byte[] PublicScript;

    public List<TXOutputWallet> OutputsValue = new();
    public List<TXOutputWallet> OutputsValueUnconfirmed = new();
    public List<TXOutputWallet> OutputsValueUnconfirmedSpent = new();
    public List<TX> HistoryTransactions = new();

    public long Balance;


    public Wallet(string privKeyDec, TypeWallet typeWallet)
    {
      Type = typeWallet;

      PrivKeyDec = privKeyDec;

      PublicKey = Crypto.GetPubKeyFromPrivKey(PrivKeyDec);

      PublicKeyHash160 = ComputeHash160Pubkey(PublicKey);

      PublicScript = PREFIX_P2PKH
        .Concat(PublicKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();
    }

    public void LoadImage(string path)
    {
      byte[] bytesImageWallet = File.ReadAllBytes(
        Path.Combine(path, "wallet"));

      SHA256 sHA256 = SHA256.Create();

      int index = 0;

      while (index < bytesImageWallet.Length)
        HistoryTransactions.Add(
          Block.ParseTX(
            bytesImageWallet, 
            ref index, 
            sHA256));
    }

    public void CreateImage(string path)
    {
      using (FileStream fileImageWallet = new(
          Path.Combine(path, "wallet"),
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
      {
        foreach(TX tX in HistoryTransactions)
        {
          byte[] txRaw = tX.TXRaw.ToArray();
          fileImageWallet.Write(txRaw, 0, txRaw.Length);
        }
      }
    }

    public void InsertBlock(Block block, Token token)
    {
      foreach (TX tX in block.TXs)
        foreach (TXOutput tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
            OutputsValue.Add(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                Index = tX.TXOutputs.IndexOf(tXOutput),
                Value = tXOutput.Value
              });

            $"AddOutput to wallet {token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, token.LogFile, token.LogEntryNotifier);

            AddTXToHistory(tX);

            Balance += tXOutput.Value;

            $"Balance of wallet {token}: {Balance}".Log(this, token.LogFile, token.LogEntryNotifier);
          }

      foreach (TX tX in block.TXs)
        foreach (TXInput tXInput in tX.TXInputs)
        {
          $"Try spend input in wallet {token} refing output: {tXInput.TXIDOutput.ToHexString()}, index {tXInput.OutputIndex}".Log(this, token.LogFile, token.LogEntryNotifier);

          TXOutputWallet tXOutputWallet = OutputsValue.Find(o =>
            o.TXID.IsEqual(tXInput.TXIDOutput) &&
            o.Index == tXInput.OutputIndex);

          if(tXOutputWallet != null)
          {
            OutputsValue.Remove(tXOutputWallet);
            AddTXToHistory(tX);
            Balance -= tXOutputWallet.Value;

            $"Remove TXOutputWalletTXID of wallet{token}: {tXOutputWallet.TXID.ToHexString()}, Index {tXOutputWallet.Index}, Value {tXOutputWallet.Value}.".Log(this, token.LogFile, token.LogEntryNotifier);

            $"Balance of wallet {token}: {Balance}".Log(this, token.LogFile, token.LogEntryNotifier);
          }
        }
    }

    public byte[] ComputeHash160Pubkey(byte[] publicKey)
    {
      byte[] publicKeyHash160 = new byte[20];

      var hashPublicKey = SHA256.ComputeHash(publicKey);
      RIPEMD160.BlockUpdate(hashPublicKey, 0, hashPublicKey.Length);
      RIPEMD160.DoFinal(publicKeyHash160, 0);

      return publicKeyHash160;
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

      byte[] scriptPubKey = new byte[LENGTH_P2PKH];

      Array.Copy(
        tXOutput.Buffer,
        tXOutput.StartIndexScript,
        scriptPubKey,
        0,
        LENGTH_P2PKH);

      return true;
    }

    void AddTXToHistory(TX tX)
    {
      if (!HistoryTransactions.Any(t => t.Hash.IsEqual(tX.Hash)))
        HistoryTransactions.Add(tX);
    }

    public List<byte> GetScriptSignature(byte[] tXRaw)
    {
      byte[] signature = Crypto.GetSignature(
      PrivKeyDec,
      tXRaw.ToArray());

      List<byte> scriptSig = new();

      scriptSig.Add((byte)(signature.Length + 1));
      scriptSig.AddRange(signature);
      scriptSig.Add(0x01);

      scriptSig.Add((byte)PublicKey.Length);
      scriptSig.AddRange(PublicKey);

      return scriptSig;
    }

    public void ReverseTXUnconfirmed(TX tX)
    {
      OutputsValueUnconfirmed.RemoveAll(t => t.TXID.Equals(tX.Hash));
      tX.TXInputs.ForEach(i => OutputsValueUnconfirmedSpent.RemoveAll(t => t.TXID.Equals(i.TXIDOutput)));
    }

    public void AddOutput(TXOutputWallet output)
    {
      OutputsValue.Add(output);
    }

    public bool TryGetOutput(
      long fee,
      out TXOutputWallet tXOutputWallet)
    {
      if (OutputsValue.Any())
      {
        long valueLargest = OutputsValue.Max(t => t.Value);

        if (valueLargest > fee)
        {
          tXOutputWallet = OutputsValue.Find(t => t.Value == valueLargest);
          OutputsValueUnconfirmedSpent.Add(tXOutputWallet);
          return true;
        }
      }

      List<TXOutputWallet> outputsValueUnconfirmedNotSpent = 
        OutputsValueUnconfirmed.Except(OutputsValueUnconfirmedSpent).ToList();

      if (outputsValueUnconfirmedNotSpent.Any())
      {
        long valueLargest = outputsValueUnconfirmedNotSpent.Max(t => t.Value);

        if (valueLargest > fee)
        {
          tXOutputWallet = outputsValueUnconfirmedNotSpent.Find(t => t.Value == valueLargest);
          OutputsValueUnconfirmedSpent.Add(tXOutputWallet);
          return true;
        }
      }

      tXOutputWallet = null;
      return false;
    }
    
    public byte[] GetReceptionScript()
    {
      byte[] script = new byte[26];

      script[0] = LENGTH_P2PKH;

      PREFIX_P2PKH.CopyTo(script, 1);
      PublicKeyHash160.CopyTo(script, 4);
      POSTFIX_P2PKH.CopyTo(script, 24);

      return script;
    }

    public void Clear()
    {
      OutputsValue.Clear();
      OutputsValueUnconfirmed.Clear();
      OutputsValueUnconfirmedSpent.Clear();
    }
  }
}