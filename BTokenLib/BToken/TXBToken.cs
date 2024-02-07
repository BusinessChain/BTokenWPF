using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace BTokenLib
{
  public class TXBToken : TX
  {
    public byte[] PubKeyCompressed;

    public const int LENGTH_IDACCOUNT = 20;
    public byte[] IDAccountSource = new byte[LENGTH_IDACCOUNT];

    public int LengthSig;
    public byte[] Signature;

    public ulong Nonce;
    public ulong NonceInDB;

    public long Value;
    public long ValueInDB;


    public override string Print()
    {
      string text = "";

      foreach (TXOutputBToken tXOutput in TXOutputs)
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

