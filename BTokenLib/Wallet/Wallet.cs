using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;


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

    public List<TXOutputWallet> OutputsValueDesc = new();
    public List<TX> HistoryTransactions = new();


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

    public void InsertBlock(Block block)
    {
      foreach (TX tX in block.TXs)
        foreach (TXOutput tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
            AddOutput(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                Index = tX.TXOutputs.IndexOf(tXOutput),
                Value = tXOutput.Value
              });

            AddTXToHistory(tX);
          }

      foreach (TX tX in block.TXs)
        foreach (TXInput tXInput in tX.TXInputs)
          if (OutputsValueDesc.RemoveAll(o =>
            o.TXID.IsEqual(tXInput.TXIDOutput) &&
            o.Index == tXInput.OutputIndex) > 0)
            AddTXToHistory(tX);
    }

    public long GetBalance()
    {
      return OutputsValueDesc.Sum(o => o.Value);
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

    public void RemoveOutput(byte[] hash)
    {
      List<TXOutputWallet> outputsRemove =
        OutputsValueDesc.FindAll(t => t.TXID.Equals(hash));

      outputsRemove.ForEach(o => {
        OutputsValueDesc.Remove(o);
      });

      int i = OutputsValueDesc.RemoveAll(t => t.TXID.Equals(hash));
    }

    public void AddOutput(TXOutputWallet output)
    {
      if (OutputsValueDesc.Any(
        o => o.TXID.IsEqual(output.TXID) && 
        o.Index == output.Index))
        return;

      int j = 0;

      while(j < OutputsValueDesc.Count && output.Value < OutputsValueDesc[j].Value)
        j += 1;

      OutputsValueDesc.Insert(j, output);
    }

    public bool TryGetOutput(
      long fee,
      out TXOutputWallet tXOutputWallet)
    {
      tXOutputWallet = null;

      if (OutputsValueDesc.Any() && 
        OutputsValueDesc[0].Value > fee)
      {
        tXOutputWallet = OutputsValueDesc[0];
        OutputsValueDesc.RemoveAt(0);
      }

      return tXOutputWallet != null;
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
      OutputsValueDesc.Clear();
    }
  }
}