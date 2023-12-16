using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BTokenLib
{
  public class TXBToken : TX
  {
    public byte[] IDAccount = new byte[32];
    public ulong Nonce;
    public int LengthScript;
    public byte[] ScriptPubKey;
  }
}
