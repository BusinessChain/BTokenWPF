using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  public class TXOutput
  {
    public byte[] Buffer;
    public int StartIndexScript;
    public int LengthScript;

    public long Value;
  }
}
