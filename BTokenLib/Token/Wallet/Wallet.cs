using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class Wallet
  {
    public SHA256 SHA256 = SHA256.Create();

    public string KeyPrivateDecimal;
    public byte[] KeyPublic;
    public byte[] Hash160PKeyPublic = new byte[20];
    public string AddressAccount;
    public byte[] PublicScript;

    public List<Token.TX> HistoryTXs = new();




    public Wallet(string privKeyDec)
    {
      KeyPrivateDecimal = privKeyDec;

      KeyPublic = Crypto.GetPubKeyFromPrivKey(KeyPrivateDecimal);

      Hash160PKeyPublic = Crypto.ComputeHash160(KeyPublic, SHA256);

      AddressAccount = Hash160PKeyPublic.BinaryToBase58Check();

      PublicScript = PREFIX_P2PKH.Concat(Hash160PKeyPublic).Concat(POSTFIX_P2PKH).ToArray();
    }

    public byte[] GetSignature(byte[] dataToBeSigned)
    {
      return Crypto.GetSignature(KeyPrivateDecimal, dataToBeSigned);
    }

    public abstract void SendTXValue(string address, long value, double feePerByte, int sequence = 0);

    public abstract void InsertBlock(Token.Block block);

    public abstract void InsertTXUnconfirmed(Token.TX tX);

    public abstract void ReverseBlock(Token.Block block);

    public abstract long GetBalance();

    public virtual void Clear()
    {
      OutputsSpendableUnconfirmed.Clear();
      OutputsSpentUnconfirmed.Clear();
    }
  }
}