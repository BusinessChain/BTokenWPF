using System;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public class TXBTokenAnchor : TXBToken
    {
      public TokenAnchor TokenAnchor = new();


      public TXBTokenAnchor(byte[] buffer, ref int index, SHA256 sHA256)
      {
        int indexTxStart = index - 1;

        ParseTXBTokenInput(buffer, ref index, sHA256);

        Array.Copy(buffer, index, TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);
        index += TokenAnchor.LENGTH_IDTOKEN;

        Array.Copy(buffer, index, TokenAnchor.HashBlockReferenced, 0, TokenAnchor.HashBlockReferenced.Length);
        index += TokenAnchor.HashBlockReferenced.Length;

        Array.Copy(buffer, index, TokenAnchor.HashBlockPreviousReferenced, 0, TokenAnchor.HashBlockPreviousReferenced.Length);
        index += TokenAnchor.HashBlockPreviousReferenced.Length;

        CountBytes = index - indexTxStart;

        Hash = sHA256.ComputeHash(sHA256.ComputeHash(buffer, indexTxStart, CountBytes));

        VerifySignatureTX(indexTxStart, buffer, ref index);
      }

      public override long GetValueOutputs()
      {
        return 0;
      }

      public override List<TokenAnchor> GetTokenAnchors()
      {
        return new() { TokenAnchor };
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
}
