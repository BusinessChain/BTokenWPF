using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  public class TXOutputBitcoin : TXOutput
  {
    public TXOutputBitcoin(
      byte[] buffer,
      ref int index)
    {
      Buffer = buffer;

      Value = BitConverter.ToInt64(buffer, index);
      index += 8;

      LengthScript = VarInt.GetInt32(Buffer, ref index);

      StartIndexScript = index;
      index += LengthScript;
    }
  }
}
