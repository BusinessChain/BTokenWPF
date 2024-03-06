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
    public uint Token;

    public const int LENGTH_PUBKEYCOMPRESSED = 33;
    public byte[] PubKeyCompressed = new byte[LENGTH_PUBKEYCOMPRESSED];

    public const int LENGTH_IDACCOUNT = 20;
    public byte[] IDAccountSource = new byte[LENGTH_IDACCOUNT];

    public int LengthSig;
    public byte[] Signature;

    public long Nonce;
    public long NonceInDB;

    public long Value;
    public long ValueInDB;

    public List<TXOutputBToken> TXOutputs = new();


    public override string Print()
    {
      string text = "";

      return text;
    }
  }
}

