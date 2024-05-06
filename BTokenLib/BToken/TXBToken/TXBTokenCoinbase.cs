using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BTokenLib
{
  public class TXBTokenCoinbase : TXBToken
  {
    public List<TXOutputBToken> TXOutputs = new();


    public TXBTokenCoinbase()
    { }


    public TXBTokenCoinbase(byte[] buffer, SHA256 sHA256)
    {
      int index = 1;

      int countOutputs = VarInt.GetInt(buffer, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(buffer, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(buffer));
    }
  }
}
