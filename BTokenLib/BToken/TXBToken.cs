using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BTokenLib
{
  public class TXBToken : TX
  {
    public byte[] IDAccountSource = new byte[32];
    public ulong Nonce;
    public int LengthSig;
    public byte[] PubKeyCompressed;
    public byte[] Signature;

    public ulong NonceInDB;

    public long Value;
    public long ValueInDB;
  }
}

