using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenAnchor : TXBToken
  {
    public TokenAnchor TokenAnchor = new();


    public TXBTokenAnchor(byte[] buffer, int startIndexMessage, ref int index, SHA256 sHA256)
    {
      ParseTXBTokenInput(buffer, ref index, sHA256);

      Array.Copy(buffer, index, TokenAnchor.IDToken, 0, TokenAnchor.IDToken.Length);
      index += TokenAnchor.IDToken.Length;

      Array.Copy(
        buffer,
        index,
        TokenAnchor.HashBlockReferenced,
        0,
        TokenAnchor.HashBlockReferenced.Length);

      index += 32;

      Array.Copy(
        buffer,
        index,
        TokenAnchor.HashBlockPreviousReferenced,
        0,
        TokenAnchor.HashBlockPreviousReferenced.Length);

      index += TokenAnchor.HashBlockPreviousReferenced.Length;

      VerifySignatureTX(buffer, startIndexMessage, ref index);
    }
  }
}
