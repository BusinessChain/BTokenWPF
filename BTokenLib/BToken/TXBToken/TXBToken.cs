﻿using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public abstract class TXBToken : TX
  {
    const int LENGTH_PUBKEYCOMPRESSED = 33;

    /// <summary>
    /// The PublicKey specifies from which account the funds are sourced.
    /// </summary>
    public byte[] PublicKey = new byte[LENGTH_PUBKEYCOMPRESSED];

    public const int LENGTH_IDACCOUNT = 20;

    /// <summary>
    /// IDAccountSource is derived from the PublicKey and is used to address the account in the database.
    /// </summary>
    public byte[] IDAccountSource = new byte[LENGTH_IDACCOUNT];

    /// <summary>
    /// In order for the transaction to be valid, the nonce must be equal as the nonce of the 
    /// account source.
    /// </summary>
    public int Nonce;

    /// <summary>
    /// This is the block height at which the account source was created.
    /// It is in a sense an extension of the nonce.
    /// </summary>
    public int BlockheightAccountInit;


    public override bool IsSuccessorTo(TX tX)
    {
      TXBToken tXBToken = tX as TXBToken;

      return tXBToken != null
        && IDAccountSource.IsAllBytesEqual(tXBToken.IDAccountSource)
        && BlockheightAccountInit == tXBToken.BlockheightAccountInit
        && Nonce == tXBToken.Nonce + 1;
    }

    //public override bool TryGetAnchorToken(out TokenAnchor tokenAnchor)
    //{
    //  TXBTokenAnchor tXBTokenAnchor = this as TXBTokenAnchor;

    //  if (tXBTokenAnchor == null)
    //    tokenAnchor = null;
    //  else
    //    tokenAnchor = tXBTokenAnchor.TokenAnchor;

    //  return tokenAnchor != null;
    //}

    public override List<TokenAnchor> GetTokenAnchors()
    {
      return new();
    }


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
    }

    public void VerifySignatureTX(int indexTxStart, byte[] buffer, ref int index)
    {
      int lengthMessage = index - indexTxStart;

      int lengthSig = buffer[index++];
      byte[] signature = new byte[lengthSig];
      Array.Copy(buffer, index, signature, 0, lengthSig);
      index += lengthSig;

      if (!Crypto.VerifySignature(buffer, indexTxStart, lengthMessage, PublicKey, signature))
        throw new ProtocolException($"TX {this} contains invalid signature.");
    }

    public virtual List<TXOutputBToken> GetOutputs()
    { return new(); }

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
        ("ValueOutputs", $"{GetValueOutputs()}"),
        ("Fee", $"{Fee}")
      };
    }
  }
}

