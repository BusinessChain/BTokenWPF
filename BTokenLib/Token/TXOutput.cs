using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class Token
  {
    public class TXOutput
    {
      public enum TypesToken
      {
        Unspecified = 0x00,
        P2PKH = 0x01,
        AnchorToken = 0x02,
        Data = 0x03
      }

      public TypesToken Type;

      public TokenAnchor TokenAnchor;
    }
  }
}
