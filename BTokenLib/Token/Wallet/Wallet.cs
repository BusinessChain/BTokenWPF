using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public abstract class Wallet
  {
    public SHA256 SHA256 = SHA256.Create();

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
      
    public abstract void SendTXValue(string address, long value, double feePerByte, int sequence = 0);

    public abstract void SendTXData(byte[] data, double feePerByte, int sequence = 0);

    public abstract void InsertBlock(Block block);

    public abstract void InsertTXUnconfirmed(TX tX);

    public abstract void ReverseBlock(Block block);

    public abstract long GetBalance();

    public virtual void Clear()
    {
      OutputsSpendableUnconfirmed.Clear();
      OutputsSpentUnconfirmed.Clear();
    }
  }
}