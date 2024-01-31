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
  }
}

