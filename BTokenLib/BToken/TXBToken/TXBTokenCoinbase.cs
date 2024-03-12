using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  public class TXBTokenCoinbase : TXBToken
  {
    public List<TXOutputBToken> TXOutputs = new();


    public TXBTokenCoinbase(byte[] buffer, ref int index)
    {
      int countOutputs = VarInt.GetInt32(buffer, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(buffer, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }
    }
  }
}
