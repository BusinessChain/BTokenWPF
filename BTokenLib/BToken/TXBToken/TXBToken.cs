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

    /// <summary>
    /// This is the block height at which the account source was created.
    /// It is in a sense an extension of the nonce.
    /// </summary>
    public int BlockheightAccountInit;

    /// <summary>
    /// In order for the transaction to be valid, the nonce must be equal as the nonce of the 
    /// account source.
    /// </summary>
    public int Nonce;

    /// <summary>
    /// This is the total transaction value that will be deducted from the source account
    /// in order to pay the transaction outputs plus fee.
    /// </summary>
    public long Value;

    public long Fee;


    public void ParseTXBTokenInput(byte[] buffer, ref int index, SHA256 sHA256)
    {
      Array.Copy(buffer, index, PublicKey, 0, PublicKey.Length);
      index += PublicKey.Length;

      IDAccountSource = Crypto.ComputeHash160(PublicKey, sHA256);

      BlockheightAccountInit = BitConverter.ToInt32(buffer, index);
      index += 4;

      Nonce = BitConverter.ToInt32(buffer, index);
      index += 4;

      Fee = BitConverter.ToInt64(buffer, index);
      index += 8;

      Value += Fee;
    }

    public void VerifySignatureTX(byte[] buffer, ref int index)
    {
      int lengthIndexMessage = index;

      int lengthSig = buffer[index++];
      byte[] signature = new byte[lengthSig];
      Array.Copy(buffer, index, signature, 0, lengthSig);
      index += lengthSig;

      if (!Crypto.VerifySignature(buffer, 0, lengthIndexMessage, PublicKey, signature))
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

    public override List<(string label, string value)> GetLabelsValuePairs()
    {
      return new List<(string label, string value)>()
      {
        ("Type", $"{GetType().Name}"),
        ("Hash", $"{this}"),
        ("IDAccountSource", $"{IDAccountSource.BinaryToBase58Check()}"),
        ("BlockheightAccountInit", $"{BlockheightAccountInit}"),
        ("Nonce", $"{Nonce}"),
        ("Value", $"{Value}"),
        ("Fee", $"{Fee}")
      };
    }
  }
}

