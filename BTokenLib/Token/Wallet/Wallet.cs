using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;


namespace BTokenLib
{
  public abstract class Wallet
  {
    protected Token Token;

    public const int LENGTH_DATA_P2PKH_INPUT = 180;
    public const int LENGTH_P2PKH = 25;
    public byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    public byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    Crypto Crypto = new();

    protected SHA256 SHA256 = SHA256.Create();
    readonly RipeMD160Digest RIPEMD160 = new();

    protected string PrivKeyDec;
    protected byte[] PublicKey;
    protected byte[] PublicKeyHash160 = new byte[20];
    public string AddressAccount;
    public byte[] PublicScript;

    public List<TX> HistoryTransactions = new();

    protected List<TXOutputWallet> OutputsUnconfirmed = new();
    protected List<TXOutputWallet> OutputsUnconfirmedSpent = new();

    public long Balance; 
    public long BalanceUnconfirmed;


    public Wallet(string privKeyDec, Token token)
    {
      Token = token;

      PrivKeyDec = privKeyDec;

      PublicKey = Crypto.GetPubKeyFromPrivKey(PrivKeyDec);

      PublicKeyHash160 = ComputeHash160Pubkey(PublicKey);

      AddressAccount = PubKeyHashToBase58Check(PublicKeyHash160);

      PublicScript = PREFIX_P2PKH
        .Concat(PublicKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();
    }

    public abstract TX CreateTX(string address, long value, long fee);

    public abstract bool CreateDataTX(
      double feeSatoshiPerByte, 
      byte[] data,
      out Token.TokenAnchor tokenAnchor);

    public static string PubKeyHashToBase58Check(byte[] pubKeyArray)
    {
      List<byte> pubKey = pubKeyArray.ToList();
      pubKey.Insert(0, 0x00);

      SHA256 sHA256 = SHA256.Create();

      byte[] checksum = sHA256.ComputeHash(
        sHA256.ComputeHash(pubKey.ToArray()));

      pubKey.AddRange(checksum.Take(4));

      return pubKey.ToArray().ToBase58String();
    }

    public static byte[] Base58CheckToPubKeyHash(string base58)
    {
      byte[] bb = base58.Base58ToByteArray();

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

    public virtual void LoadImage(string path)
    {
      byte[] fileWalletHistoryTransactions = File.ReadAllBytes(
        Path.Combine(path, "walletHistoryTransactions"));

      SHA256 sHA256 = SHA256.Create();

      int index = 0;

      while (index < fileWalletHistoryTransactions.Length)
        HistoryTransactions.Add(
          Token.ParseTX(
            fileWalletHistoryTransactions, 
            ref index, 
            sHA256));

      LoadOutputs(OutputsUnconfirmed, Path.Combine(path, "OutputsValueUnconfirmed"));
      LoadOutputs(OutputsUnconfirmedSpent, Path.Combine(path, "OutputsValueUnconfirmedSpent"));
    }

    protected static void LoadOutputs(List<TXOutputWallet> outputs, string fileName)
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

    public virtual void CreateImage(string path)
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

      StoreOutputs(OutputsUnconfirmed, Path.Combine(path, "OutputsValueUnconfirmed"));
      StoreOutputs(OutputsUnconfirmedSpent, Path.Combine(path, "OutputsValueUnconfirmedSpent"));
    }

    protected static void StoreOutputs(List<TXOutputWallet> outputs, string fileName)
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

    public abstract void InsertBlock(Block block);

    public byte[] ComputeHash160Pubkey(byte[] publicKey)
    {
      byte[] publicKeyHash160 = new byte[20];

      var hashPublicKey = SHA256.ComputeHash(publicKey);
      RIPEMD160.BlockUpdate(hashPublicKey, 0, hashPublicKey.Length);
      RIPEMD160.DoFinal(publicKeyHash160, 0);

      return publicKeyHash160;
    }

    protected bool TryDetectTXOutputSpendable(TXOutput tXOutput)
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

    protected void AddTXToHistory(TX tX)
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

    public void AddOutputUnconfirmed(TXOutputWallet output)
    {
      OutputsUnconfirmed.Add(output);
      BalanceUnconfirmed += output.Value;
    }

    public abstract void ReverseTXUnconfirmed(TX tX);

    public byte[] GetReceptionScript()
    {
      byte[] script = new byte[26];

      script[0] = LENGTH_P2PKH;

      PREFIX_P2PKH.CopyTo(script, 1);
      PublicKeyHash160.CopyTo(script, 4);
      POSTFIX_P2PKH.CopyTo(script, 24);

      return script;
    }

    public virtual void Clear()
    {
      OutputsUnconfirmed.Clear();
      OutputsUnconfirmedSpent.Clear();

      Balance = 0;
      BalanceUnconfirmed = 0;
    }
  }
}