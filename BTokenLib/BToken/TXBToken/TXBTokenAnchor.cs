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


    public TXBTokenAnchor(byte[] tXRaw, SHA256 sHA256)
    {
      int index = 1;
      ParseTXBTokenInput(tXRaw, ref index, sHA256);

      Array.Copy(tXRaw, index, TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);
      index += TokenAnchor.LENGTH_IDTOKEN;

      Array.Copy(
        tXRaw,
        index,
        TokenAnchor.HashBlockReferenced,
        0,
        TokenAnchor.HashBlockReferenced.Length);

      index += 32;

      Array.Copy(
        tXRaw,
        index,
        TokenAnchor.HashBlockPreviousReferenced,
        0,
        TokenAnchor.HashBlockPreviousReferenced.Length);

      index += TokenAnchor.HashBlockPreviousReferenced.Length;

      VerifySignatureTX(tXRaw, ref index);

      TXRaw = tXRaw.ToList();

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(tXRaw));
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
