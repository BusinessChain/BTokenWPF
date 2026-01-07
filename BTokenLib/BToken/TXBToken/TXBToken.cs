using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public class TXBToken : TX
    {
      const int LENGTH_PUBKEYCOMPRESSED = 33;

      /// <summary>
      /// The PublicKey specifies from which account the funds are sourced.
      /// </summary>
      public byte[] KeyPublic = new byte[LENGTH_PUBKEYCOMPRESSED];

      public const int LENGTH_IDACCOUNT = 20;

      /// <summary>
      /// IDAccountSource is derived from the PublicKey and is used to address the account in the database.
      /// </summary>
      public byte[] IDAccountSource = new byte[LENGTH_IDACCOUNT];

      /// <summary>
      /// In order for the transaction to be valid, the nonce must be equal to the nonce of the 
      /// account source.
      /// </summary>
      public int Nonce;

      /// <summary>
      /// This has to match the block height at which the account source was created.
      /// It is in a sense an extension of the nonce.
      /// </summary>
      public int BlockheightAccountCreated;


      public List<TXOutputBToken> TXOutputs = new();


      public override bool IsSuccessorTo(TX tX)
      {
        TXBToken tXBToken = tX as TXBToken;

        return tXBToken != null
          && IDAccountSource.IsAllBytesEqual(tXBToken.IDAccountSource)
          && BlockheightAccountCreated == tXBToken.BlockheightAccountCreated
          && Nonce == tXBToken.Nonce + 1;
      }

      public override List<TokenAnchor> GetTokenAnchors()
      {
        return new();
      }

      public void ParseTXBTokenInput(byte[] buffer, ref int index, SHA256 sHA256)
      {
        Array.Copy(buffer, index, KeyPublic, 0, KeyPublic.Length);
        index += KeyPublic.Length;

        IDAccountSource = Crypto.ComputeHash160(KeyPublic, sHA256);

        BlockheightAccountCreated = BitConverter.ToInt32(buffer, index);
        index += 4;

        Nonce = BitConverter.ToInt32(buffer, index);
        index += 4;

        Fee = BitConverter.ToInt64(buffer, index);
        index += 8;
      }

      public void VerifySignatureTX(int indexTxStart, byte[] buffer, ref int index)
      {
        int lengthMessage = index - indexTxStart;

        int lengthSig = buffer[index++];
        byte[] signature = new byte[lengthSig];
        Array.Copy(buffer, index, signature, 0, lengthSig);
        index += lengthSig;

        if (!Crypto.VerifySignature(buffer, indexTxStart, lengthMessage, KeyPublic, signature))
          throw new ProtocolException($"TX {this} contains invalid signature.");
      }

      public override List<(string label, string value)> GetLabelsValuePairs()
      {
        List<(string label, string value)> labelValuePairs = new List<(string label, string value)>()
      {
        ("Type", $"{GetType().Name}"),
        ("Hash", $"{this}"),
        ("IDAccountSource", $"{IDAccountSource.BinaryToBase58Check()}"),
        ("BlockheightAccountInit", $"{BlockheightAccountCreated}"),
        ("Nonce", $"{Nonce}"),
        ("ValueOutputs", $"{GetValueOutputs()}"),
        ("Fee", $"{Fee}")
      };

        for (int i = 0; i < TXOutputs.Count; i++)
        {
          TXOutputBToken output = TXOutputs[i];

          labelValuePairs.Add(($"Output{i} :: IDAccount", $"{output.IDAccount.BinaryToBase58Check()}"));
          labelValuePairs.Add(($"Output{i} :: Value", $"{output.Value}"));
        }

        return labelValuePairs;
      }

      public override long GetValueOutputs()
      {
        return TXOutputs.Sum(t => t.Value);
      }

      public void Serialize(Wallet wallet)
      {
        Serialize();

        byte[] signature = wallet.GetSignature(TXRaw);

        List<byte> tXRaw = TXRaw.ToList();

        tXRaw.Add((byte)signature.Length);
        tXRaw.AddRange(signature);

        TXRaw = tXRaw.ToArray();
      }

      public void Serialize()
      {
        List<byte> tXRaw = new();

        tXRaw.AddRange(KeyPublic);
        tXRaw.AddRange(BitConverter.GetBytes(BlockheightAccountCreated));
        tXRaw.AddRange(BitConverter.GetBytes(Nonce));
        tXRaw.AddRange(BitConverter.GetBytes(Fee));

        tXRaw.Add((byte)TXOutputs.Count);
        foreach (TXOutputBToken output in TXOutputs)
        {
          tXRaw.Add((byte)output.Type);
          tXRaw.AddRange(output.Script);
        }

        TXRaw = tXRaw.ToArray();
      }
    }
  }
}

