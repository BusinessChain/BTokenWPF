using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenValueTransfer : TXBToken
  {
    public TXBTokenValueTransfer(byte[] buffer, ref int index, SHA256 sHA256)
    {
      int indexTxStart = index - 1;

      ParseTXBTokenInput(buffer, ref index, sHA256);

      int countOutputs = VarInt.GetInt(buffer, ref index);

      for (int i = 0; i < countOutputs; i++)
        TXOutputs.Add(new(buffer, ref index));

      CountBytes = index - indexTxStart;

      Hash = sHA256.ComputeHash(sHA256.ComputeHash(buffer, indexTxStart, CountBytes));

      VerifySignatureTX(indexTxStart, buffer, ref index);
    }
  }
}
