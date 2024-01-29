using System;

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;

using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X9;
using System.Linq;

namespace BTokenLib
{
  public static class Crypto
  {       
    public static bool VerifySignature(
      byte[] buffer,
      int startIndex,
      byte[] publicKey,
      byte[] signature)
    {
      X9ECParameters curve = SecNamedCurves.GetByName("secp256k1");

      // Experimentiere mit komprimiertem pubkey.
      //Org.BouncyCastle.Math.EC.ECPoint bla = curve.Curve.DecodePoint(publicKey);
      //bla.get

      ECPublicKeyParameters keyParameters = new(
        curve.Curve.DecodePoint(publicKey),
        new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H));

      ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");

      signer.Init(false, keyParameters);
      signer.BlockUpdate(buffer, startIndex, buffer.Length - startIndex);

      return signer.VerifySignature(signature);
    }

    //static ECDomainParameters SPEC = ECNamedCurveTable.getParameterSpec("secp256k1");

    //static byte[] compressedToUncompressed(byte[] compKey)
    //{
    //  ECPoint point = SPEC.getCurve().decodePoint(compKey);
    //  byte[] x = point.getXCoord().getEncoded();
    //  byte[] y = point.getYCoord().getEncoded();
    //  // concat 0x04, x, and y, make sure x and y has 32-bytes:
    //  return concat(new byte[] { 0x04 }, x, y);
    //}

    public static byte[] GetSignature(
      string privateKey, 
      byte[] message,
      SHA256 sHA256)
    {      
      message = sHA256.ComputeHash(message, 0, message.Length);

      var curve = SecNamedCurves.GetByName("secp256k1");

      ECPrivateKeyParameters keyParameters = new(
        new BigInteger(privateKey),
        new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H));

      ISigner signer = SignerUtilities.GetSigner("SHA-256withECDSA");

      while (true)
      {
        signer.Init(true, keyParameters);
        signer.BlockUpdate(message, 0, message.Length);

        byte[] signature = signer.GenerateSignature();

        if (signature[signature[3] + 5] > 32)
          continue;

        return signature;
      }
    }

    public static byte[] GetPubKeyFromPrivKey(string privKey)
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

    public static byte[] ComputeHash160(byte[] data, SHA256 sHA256)
    {
      byte[] publicKeyHash160 = new byte[20];

      var hashPublicKey = sHA256.ComputeHash(data);

      RipeMD160Digest RIPEMD160 = new();
      RIPEMD160.BlockUpdate(hashPublicKey, 0, hashPublicKey.Length);
      RIPEMD160.DoFinal(publicKeyHash160, 0);

      return publicKeyHash160;
    }
  }
}
