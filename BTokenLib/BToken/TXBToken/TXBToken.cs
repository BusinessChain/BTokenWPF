using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Security.Cryptography;

namespace BTokenLib
{
  public abstract class TXBToken : TX
  {
    public const int LENGTH_PUBKEYCOMPRESSED = 33;
    public byte[] PublicKey = new byte[LENGTH_PUBKEYCOMPRESSED];

    public const int LENGTH_IDACCOUNT = 20;
    public byte[] IDAccountSource = new byte[LENGTH_IDACCOUNT];

    public int LengthSig;
    public byte[] Signature;

    public long Nonce;
    public long NonceInDB;

    public long Value;
    public long ValueInDB;


    public void ParseTXBTokenInput(byte[] buffer, ref int index, SHA256 sHA256)
    {
      Array.Copy(buffer, index, PublicKey, 0, PublicKey.Length);
      index += PublicKey.Length;

      IDAccountSource = Crypto.ComputeHash160(PublicKey, sHA256);

      Nonce = BitConverter.ToInt64(buffer, index);
      index += 8;

      Fee = BitConverter.ToInt64(buffer, index);
      index += 8;

      Value += Fee;
    }

    public void VerifySignatureTX(byte[] buffer, int startIndexMessage, ref int index)
    {
      int lengthSig = buffer[index++];
      Signature = new byte[lengthSig];
      Array.Copy(buffer, index, Signature, 0, lengthSig);
      index += lengthSig;

      if (!Crypto.VerifySignature(
        buffer,
        startIndexMessage,
        index - startIndexMessage,
        PublicKey,
        Signature))
        throw new ProtocolException($"TX {this} contains invalid signature.");
    }

    public override string Print()
    {
      string text = "";

      return text;
    }
  }
}

