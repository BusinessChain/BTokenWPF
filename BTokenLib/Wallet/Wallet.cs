using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;


namespace BTokenLib
{
  public class Base58
  {
    /// <summary>
    /// Converts a base-58 string to a byte array, returning null if it wasn't valid.
    /// </summary>
    public static byte[] ToByteArray(string base58)
    {
      Org.BouncyCastle.Math.BigInteger bi2 = new Org.BouncyCastle.Math.BigInteger("0");
      string b58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

      foreach (char c in base58)
      {
        if (b58.IndexOf(c) != -1)
        {
          bi2 = bi2.Multiply(new Org.BouncyCastle.Math.BigInteger("58"));
          bi2 = bi2.Add(new Org.BouncyCastle.Math.BigInteger(b58.IndexOf(c).ToString()));
        }
        else
        {
          return null;
        }
      }

      byte[] bb = bi2.ToByteArrayUnsigned();

      // interpret leading '1's as leading zero bytes
      foreach (char c in base58)
      {
        if (c != '1') break;
        byte[] bbb = new byte[bb.Length + 1];
        Array.Copy(bb, 0, bbb, 1, bb.Length);
        bb = bbb;
      }

      return bb;
    }

    public static string FromByteArray(byte[] ba)
    {
      Org.BouncyCastle.Math.BigInteger addrremain = new Org.BouncyCastle.Math.BigInteger(1, ba);

      Org.BouncyCastle.Math.BigInteger big0 = new Org.BouncyCastle.Math.BigInteger("0");
      Org.BouncyCastle.Math.BigInteger big58 = new Org.BouncyCastle.Math.BigInteger("58");

      string b58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

      string rv = "";

      while (addrremain.CompareTo(big0) > 0)
      {
        int d = Convert.ToInt32(addrremain.Mod(big58).ToString());
        addrremain = addrremain.Divide(big58);
        rv = b58.Substring(d, 1) + rv;
      }

      // handle leading zeroes
      foreach (byte b in ba)
      {
        if (b != 0) break;
        rv = "1" + rv;

      }
      return rv;
    }

  }


  public partial class Wallet
  {
    public enum TypeWallet
    {
      AccountType,
      UTXOType
    }

    TypeWallet Type;

    public const int LENGTH_DATA_P2PKH_INPUT = 180;
    const int LENGTH_P2PKH = 25;
    byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    Crypto Crypto = new();

    SHA256 SHA256 = SHA256.Create();
    readonly RipeMD160Digest RIPEMD160 = new();

    string PrivKeyDec;
    byte[] PublicKey;
    byte[] PublicKeyHash160 = new byte[20];
    public string AddressAccount;
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

      AddressAccount = PubKeyHashToBase58Check(PublicKeyHash160);

      PublicScript = PREFIX_P2PKH
        .Concat(PublicKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();
    }

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

    public static string PubKeyHashToBase58Check(byte[] pubKeyArray)
    {
      List<byte> pubKey = pubKeyArray.ToList();
      pubKey.Insert(0, 0x00);

      SHA256 sHA256 = SHA256.Create();

      byte[] checksum = sHA256.ComputeHash(
        sHA256.ComputeHash(pubKey.ToArray()));

      pubKey.AddRange(checksum.Take(4));

      return Base58.FromByteArray(pubKey.ToArray());
    }

    public static byte[] Base58CheckToPubKeyHash(string base58)
    {
      byte[] bb = Base58.ToByteArray(base58);

      Sha256Digest bcsha256a = new();
      bcsha256a.BlockUpdate(bb, 0, bb.Length - 4);

      byte[] checksum = new byte[32];
      bcsha256a.DoFinal(checksum, 0);
      bcsha256a.BlockUpdate(checksum, 0, 32);
      bcsha256a.DoFinal(checksum, 0);

      for (int i = 0; i < 4; i++)
        if (checksum[i] != bb[bb.Length - 4 + i])
          throw new Exception($"Invalid checksum in address {base58}.");

      byte[] rv = new byte[bb.Length - 5];
      Array.Copy(bb, 1, rv, 0, bb.Length - 5);
      return rv;
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

    public List<TXOutputWallet> GetOutputs(double feeSatoshiPerByte, out long feeOutputs)
    {
      long fee = (long)feeSatoshiPerByte * LENGTH_DATA_P2PKH_INPUT;

      List<TXOutputWallet> outputsValueNotSpent = 
        Outputs.Where(o => o.Value > fee)
        .Concat(OutputsUnconfirmed.Where(o => o.Value > fee))
        .Except(OutputsUnconfirmedSpent)
        .Take(VarInt.PREFIX_UINT16 - 1).ToList();

      OutputsUnconfirmedSpent.AddRange(outputsValueNotSpent);

      outputsValueNotSpent.ForEach(o => BalanceUnconfirmed -= o.Value);

      feeOutputs = fee * outputsValueNotSpent.Count;
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