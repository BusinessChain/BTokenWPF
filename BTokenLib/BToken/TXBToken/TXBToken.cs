using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;

namespace BTokenLib
{
  public abstract class TXBToken : TX
  {
    public const int LENGTH_PUBKEYCOMPRESSED = 33;
    public byte[] PublicKey = new byte[LENGTH_PUBKEYCOMPRESSED];

    public const int LENGTH_IDACCOUNT = 20;
    public byte[] IDAccountSource = new byte[LENGTH_IDACCOUNT];

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

    public void VerifySignatureTX(byte[] buffer, ref int index)
    {
      int lengthSig = buffer[index++];
      byte[] signature = new byte[lengthSig];
      Array.Copy(buffer, index, signature, 0, lengthSig);
      index += lengthSig;

      if (!Crypto.VerifySignature(
        buffer,
        0,
        index,
        PublicKey,
        signature))
        throw new ProtocolException($"TX {this} contains invalid signature.");
    }

    public override void WriteToStream(Stream stream)
    {
      List<byte> tXRawSerialized = TXRaw.ToList();
      tXRawSerialized.InsertRange(0, VarInt.GetBytes(TXRaw.Count));

      stream.Write(tXRawSerialized.ToArray(), 0, tXRawSerialized.Count);
    }

    public override string Print()
    {
      string text = "";

      return text;
    }
  }
}

