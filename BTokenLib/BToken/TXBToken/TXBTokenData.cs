using System;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenData : TXBToken
  {
    public byte[] Data;


    public TXBTokenData(byte[] buffer, ref int index, SHA256 sHA256)
    {
      int indexTxStart = index - 1;

      ParseTXBTokenInput(buffer, ref index, sHA256);

      Data = new byte[VarInt.GetInt(buffer, ref index)];

      Array.Copy(buffer, index, Data, 0, Data.Length);

      index += Data.Length;

      CountBytes = index - indexTxStart;

      Hash = sHA256.ComputeHash(sHA256.ComputeHash(buffer, indexTxStart, CountBytes));

      VerifySignatureTX(indexTxStart, buffer, ref index);
    }

    public override long GetValueOutputs()
    {
      return 0;
    }
  }
}
