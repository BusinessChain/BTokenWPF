using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenAnchor : TXBToken
  {
    public TokenAnchor TokenAnchor = new();


    public TXBTokenAnchor(byte[] buffer, SHA256 sHA256)
    {
      int index = 1;
      ParseTXBTokenInput(buffer, ref index, sHA256);

      Array.Copy(buffer, index, TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);
      index += TokenAnchor.LENGTH_IDTOKEN;

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

      VerifySignatureTX(buffer, ref index);

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(buffer));
    }

    public override List<(string label, string value)> GetLabelsValuePairs()
    {
      List<(string label, string value)> labelValuePairs = base.GetLabelsValuePairs();

      labelValuePairs.Add(($"IDToken", $"{TokenAnchor.IDToken.ToHexString()}"));
      labelValuePairs.Add(($"HashBlockReferenced", $"{TokenAnchor.HashBlockReferenced.ToHexString()}"));
      labelValuePairs.Add(($"HashBlockPreviousReferenced", $"{TokenAnchor.HashBlockPreviousReferenced.ToHexString()}"));

      return labelValuePairs;
    }
  }
}
