using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;


namespace BTokenLib
{
  public class TXBitcoin : TX
  {
    public List<TXInput> Inputs = new();
    public List<TXOutputBitcoin> TXOutputs = new();


    public override string Print()
    {
      string text = "";

      return text;
    }

    public override void WriteToStream(Stream stream)
    {
      stream.Write(TXRaw.ToArray(), 0, TXRaw.Count);
    }
  }
}
