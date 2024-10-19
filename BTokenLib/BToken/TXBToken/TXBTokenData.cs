using System;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenData : TXBToken
  {
    public byte[] Data;


    public TXBTokenData(byte[] tXRaw, ref int index, SHA256 sHA256)
    {
      ParseTXBTokenInput(tXRaw, ref index, sHA256);

      Data = new byte[VarInt.GetInt(tXRaw, ref index)];

      Array.Copy(tXRaw, index, Data, 0, Data.Length);

      index += Data.Length;

      VerifySignatureTX(tXRaw, ref index);

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(tXRaw));
    }
  }
}
