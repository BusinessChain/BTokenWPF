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
    public List<TXOutputBitcoin> TXOutputs = new();


    public override string Print()
    {
      string text = "";

      return text;
    }

  }
}
