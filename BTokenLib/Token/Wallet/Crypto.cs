﻿using System;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class Crypto
  {
    SHA256 SHA256 = SHA256.Create();
       
    bool VerifySignature(
      byte[] message,
      byte[] publicKey,
      byte[] signature)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");
      var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);

      var q = curve.Curve.DecodePoint(publicKey);

      var keyParameters = new ECPublicKeyParameters(
        q,
        domain);

      ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");

      signer.Init(false, keyParameters);
      signer.BlockUpdate(message, 0, message.Length);

      return signer.VerifySignature(signature);
    }

    public byte[] GetSignature(
      string privateKey, 
      byte[] message)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");

      var domain = new ECDomainParameters(
        curve.Curve, 
        curve.G, 
        curve.N, 
        curve.H);
      
      message = SHA256.ComputeHash(
        message,
        0,
        message.Length);

      ISigner signer = SignerUtilities
        .GetSigner("SHA-256withECDSA");
      
      var keyParameters = new ECPrivateKeyParameters(
        new Org.BouncyCastle.Math.BigInteger(privateKey),
        domain);

      while (true)
      {
        signer.Init(true, keyParameters);
        signer.BlockUpdate(message, 0, message.Length);

        byte[] signature = signer.GenerateSignature();

        if (IsSValueTooHigh(signature))
        {
          continue;
        }

        return signature;
      }
    }

    bool IsSValueTooHigh(byte[] signature)
    {
      int lengthR = signature[3];
      int lengthSValue = signature[3 + lengthR + 2];

      return lengthSValue > 32;
    }

    public byte[] GetPubKeyFromPrivKey(
      string privKey)
    {
      var curve = SecNamedCurves.GetByName("secp256k1");

      var domain = new ECDomainParameters(
        curve.Curve, 
        curve.G, 
        curve.N, 
        curve.H);

      var d = new Org.BouncyCastle.Math.BigInteger(privKey);
      var q = domain.G.Multiply(d);

      var publicKey = new ECPublicKeyParameters(q, domain);
      return publicKey.Q.GetEncoded();
    }
  }
}
