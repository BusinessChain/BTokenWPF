using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

// Remove this
using Org.BouncyCastle.Crypto.Digests;


namespace BTokenLib
{
  public abstract class Wallet
  {
    protected SHA256 SHA256 = SHA256.Create();

    public string PrivKeyDec;
    public byte[] PublicKey;
    public byte[] PublicKeyHash160 = new byte[20];
    public string AddressAccount;

    public List<TX> HistoryTransactions = new();

    protected List<TXOutputWallet> OutputsUnconfirmed = new();
    protected List<TXOutputWallet> OutputsUnconfirmedSpent = new();

    public long Balance;


    public Wallet(string privKeyDec)
    {
      PrivKeyDec = privKeyDec;

      PublicKey = Crypto.GetPubKeyFromPrivKey(PrivKeyDec);

      PublicKeyHash160 = Crypto.ComputeHash160(PublicKey, SHA256);

      AddressAccount = PubKeyHashToBase58Check(PublicKeyHash160);
    }

    public abstract bool TryCreateTX(string address, long value, double feePerByte, out TX tX);

    public abstract bool TryCreateTXData(byte[] data, int sequence, double feePerByte, out TX tX);

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

    public string PubKeyHashToBase58Check(byte[] pubKeyArray)
    {
      List<byte> pubKey = pubKeyArray.ToList();
      pubKey.Insert(0, 0x00);

      byte[] checksum = SHA256.ComputeHash(
        SHA256.ComputeHash(pubKey.ToArray()));

      pubKey.AddRange(checksum.Take(4));

      return pubKey.ToArray().ToBase58String();
    }
       
    public virtual void LoadImage(string path)
    {
      byte[] fileWalletHistoryTransactions = File.ReadAllBytes(
        Path.Combine(path, "walletHistoryTransactions"));

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

    protected void AddTXToHistory(TX tX)
    {
      if (!HistoryTransactions.Any(t => t.Hash.IsEqual(tX.Hash)))
        HistoryTransactions.Add(tX);
    }

    public virtual void Clear()
    {
      OutputsUnconfirmed.Clear();
      OutputsUnconfirmedSpent.Clear();

      Balance = 0;
    }
  }
}