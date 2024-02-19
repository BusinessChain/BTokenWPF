using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class TXBitcoin : TX
  {
    public List<TXInput> Inputs = new();


    public override string Print()
    {
      string text = "";

      foreach (TXOutputBitcoin tXOutput in TXOutputs)
        if (tXOutput.Value == 0)
        {
          text += $"\t{this}\t";

          int index = tXOutput.StartIndexScript + 4;

          text += $"\t{tXOutput.Buffer.Skip(index).Take(32).ToArray().ToHexString()}\n";
        }

      return text;
    }

  }
}
