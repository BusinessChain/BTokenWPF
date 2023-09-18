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

    public List<TXOutputWallet> Outputs = new();
    public List<TXOutputWallet> OutputsUnconfirmed = new();
    public List<TXOutputWallet> OutputsUnconfirmedSpent = new();
    public List<TX> HistoryTransactions = new();

    public long Balance; 
    public long BalanceUnconfirmed;


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
      byte[] fileWalletHistoryTransactions = File.ReadAllBytes(
        Path.Combine(path, "walletHistoryTransactions"));

      SHA256 sHA256 = SHA256.Create();

      int index = 0;

      while (index < fileWalletHistoryTransactions.Length)
        HistoryTransactions.Add(
          Block.ParseTX(
            fileWalletHistoryTransactions, 
            ref index, 
            sHA256));

      LoadOutputs(Outputs, Path.Combine(path, "OutputsValue"));
      LoadOutputs(OutputsUnconfirmed, Path.Combine(path, "OutputsValueUnconfirmed"));
      LoadOutputs(OutputsUnconfirmedSpent, Path.Combine(path, "OutputsValueUnconfirmedSpent"));
    }

    static void LoadOutputs(List<TXOutputWallet> outputs, string fileName)
    {
      int index = 0;

      byte[] buffer = File.ReadAllBytes(fileName);

      while (index < buffer.Length)
      {
        var tXOutput = new TXOutputWallet();

        tXOutput.TXID = new byte[32];
        Array.Copy(buffer, index, tXOutput.TXID, 0, 32);
        index += 32;

        tXOutput.Index = BitConverter.ToInt32(buffer, index);
        index += 4;

        tXOutput.Value = BitConverter.ToInt64(buffer, index);
        index += 8;

        outputs.Add(tXOutput);
      }
    }

    public void CreateImage(string path)
    {
      using (FileStream fileWalletHistoryTransactions = new(
        Path.Combine(path, "walletHistoryTransactions"),
        FileMode.Create,
        FileAccess.Write,
        FileShare.None))
      {
        foreach(TX tX in HistoryTransactions)
        {
          byte[] txRaw = tX.TXRaw.ToArray();
          fileWalletHistoryTransactions.Write(txRaw, 0, txRaw.Length);
        }
      }

      StoreOutputs(Outputs, Path.Combine(path, "OutputsValue"));
      StoreOutputs(OutputsUnconfirmed, Path.Combine(path, "OutputsValueUnconfirmed"));
      StoreOutputs(OutputsUnconfirmedSpent, Path.Combine(path, "OutputsValueUnconfirmedSpent"));
    }

    static void StoreOutputs(List<TXOutputWallet> outputs, string fileName)
    {
      using (FileStream file = new(
        fileName,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None))
      {
        foreach (TXOutputWallet tXOutput in outputs)
        {
          file.Write(tXOutput.TXID, 0, tXOutput.TXID.Length);

          byte[] outputIndex = BitConverter.GetBytes(tXOutput.Index);
          file.Write(outputIndex, 0, outputIndex.Length);

          byte[] value = BitConverter.GetBytes(tXOutput.Value);
          file.Write(value, 0, value.Length);
        }
      }
    }

    public void InsertBlock(Block block, Token token)
    {
      foreach (TX tX in block.TXs)
        foreach (TXOutput tXOutput in tX.TXOutputs)
          if (tXOutput.Value > 0 && TryDetectTXOutputSpendable(tXOutput))
          {
            Outputs.Add(
              new TXOutputWallet
              {
                TXID = tX.Hash,
                Index = tX.TXOutputs.IndexOf(tXOutput),
                Value = tXOutput.Value
              });

            TXOutputWallet outputValueUnconfirmed = OutputsUnconfirmed.Find(o => o.TXID.IsEqual(tX.Hash));
            if(outputValueUnconfirmed != null)
            {
              BalanceUnconfirmed -= outputValueUnconfirmed.Value;
              OutputsUnconfirmed.Remove(outputValueUnconfirmed);
            }
            
            $"AddOutput to wallet {token}, TXID: {tX.Hash.ToHexString()}, Index {tX.TXOutputs.IndexOf(tXOutput)}, Value {tXOutput.Value}".Log(this, token.LogFile, token.LogEntryNotifier);

            AddTXToHistory(tX);

            Balance += tXOutput.Value;

            $"Balance of wallet {token}: {Balance}".Log(this, token.LogFile, token.LogEntryNotifier);
          }

      foreach (TX tX in block.TXs)
        foreach (TXInput tXInput in tX.TXInputs)
        {
          $"Try spend input in wallet {token} refing output: {tXInput.TXIDOutput.ToHexString()}, index {tXInput.OutputIndex}".Log(this, token.LogFile, token.LogEntryNotifier);

          TXOutputWallet tXOutputWallet = Outputs.Find(o =>
            o.TXID.IsEqual(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

          TXOutputWallet outputValueUnconfirmedSpent = OutputsUnconfirmedSpent
            .Find(o => o.TXID.IsEqual(tXInput.TXIDOutput) && o.Index == tXInput.OutputIndex);

          if (outputValueUnconfirmedSpent != null)
          {
            OutputsUnconfirmedSpent.Remove(outputValueUnconfirmedSpent);
            BalanceUnconfirmed += outputValueUnconfirmedSpent.Value;
          }

          if (tXOutputWallet != null)
          {
            Outputs.Remove(tXOutputWallet);
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

    public void AddOutputUnconfirmed(TXOutputWallet output)
    {
      OutputsUnconfirmed.Add(output);
      BalanceUnconfirmed += output.Value;
    }

    public List<TXOutputWallet> GetOutputs(long fee)
    {
      List<TXOutputWallet> outputsValueNotSpent = 
        Outputs.Where(o => o.Value > fee)
        .Concat(OutputsUnconfirmed.Where(o => o.Value > fee))
        .Except(OutputsUnconfirmedSpent)
        .Take(VarInt.PREFIX_UINT16 - 1).ToList();

      OutputsUnconfirmedSpent.AddRange(outputsValueNotSpent);

      outputsValueNotSpent.ForEach(o => BalanceUnconfirmed -= o.Value);

      return outputsValueNotSpent;
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
      Outputs.Clear();
      OutputsUnconfirmed.Clear();
      OutputsUnconfirmedSpent.Clear();

      Balance = 0;
      BalanceUnconfirmed = 0;
    }

  }
}