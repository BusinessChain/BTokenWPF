using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public abstract class Wallet
  {
    protected SHA256 SHA256 = SHA256.Create();

    public string KeyPrivateDecimal;
    public byte[] KeyPublic;
    public byte[] Hash160PKeyPublic = new byte[20];
    public string AddressAccount;

    public List<TX> HistoryTXs = new();

    public List<TXOutputWallet> OutputsSpendableUnconfirmed = new();

    /// <summary>
    /// Contains outputs that are spent by unconfirmed transactions. The outputs themselves might origin from confirmed and unconfirmed transactions.
    /// </summary>
    public List<TXOutputWallet> OutputsSpentUnconfirmed = new();


    public Wallet(string privKeyDec)
    {
      KeyPrivateDecimal = privKeyDec;

      KeyPublic = Crypto.GetPubKeyFromPrivKey(KeyPrivateDecimal);

      Hash160PKeyPublic = Crypto.ComputeHash160(KeyPublic, SHA256);

      AddressAccount = Hash160PKeyPublic.BinaryToBase58Check();
    }


    public byte[] GetSignature(byte[] dataToBeSigned)
    {
      return Crypto.GetSignature(KeyPrivateDecimal, dataToBeSigned);
    }
      

    public abstract bool TrySendTXValue(string address, long value, double feePerByte, out string errorMessage);

    public abstract bool TrySendTXData(byte[] data, double feePerByte);

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
      using (FileStream fileWalletHistoryTXs = new(
        Path.Combine(path, "walletHistoryTXs"),
        FileMode.Create,
        FileAccess.Write,
        FileShare.None))
      {
        foreach(TX tX in HistoryTXs)
        {
          byte[] txRaw = tX.TXRaw;
          fileWalletHistoryTXs.Write(txRaw, 0, txRaw.Length);
        }
      }

      StoreOutputs(OutputsSpendableUnconfirmed, Path.Combine(path, "OutputsValueUnconfirmed"));
      StoreOutputs(OutputsSpentUnconfirmed, Path.Combine(path, "OutputsValueUnconfirmedSpent"));
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

    public abstract void InsertTX(TX tX, int heightBlock);

    public abstract void ReverseBlock(Block block);

    public void AddTXToHistory(TX tX)
    {
      if (!HistoryTXs.Any(t => t.Hash.IsAllBytesEqual(tX.Hash)))
        HistoryTXs.Add(tX);
    }

    public abstract long GetBalance();

    public virtual void Clear()
    {
      OutputsSpendableUnconfirmed.Clear();
      OutputsSpentUnconfirmed.Clear();
    }
  }
}