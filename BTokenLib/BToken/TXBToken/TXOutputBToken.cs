using System;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public class TXOutputBToken
    {
      public enum TypesToken
      {
        Unspecified = 0x00,
        P2PKH = 0x01,
        AnchorToken = 0x02,
        Data = 0x03
      }

      public TypesToken Type;

      public long Value;

      public byte[] IDAccount;

      public byte[] Data;

      public TokenAnchor TokenAnchor = new();

      public byte[] Script;


      public TXOutputBToken() 
      { }

      public TXOutputBToken(byte[] buffer, ref int index)
      {
        Type = (TypesToken)buffer[index];
        index += 1;

        if(Type == TypesToken.P2PKH)
        {
          Value = BitConverter.ToInt64(buffer, index);
          index += 8;

          IDAccount = new byte[TXBToken.LENGTH_IDACCOUNT];

          Array.Copy(buffer, index, IDAccount, 0, TXBToken.LENGTH_IDACCOUNT);
          index += TXBToken.LENGTH_IDACCOUNT;
        }
        else if(Type == TypesToken.Data)
        {
          Data = new byte[VarInt.GetInt(buffer, ref index)];
          Array.Copy(buffer, index, Data, 0, Data.Length);
          index += Data.Length;
        }
        else if(Type == TypesToken.AnchorToken)
        {
          Array.Copy(buffer, index, TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);
          index += TokenAnchor.LENGTH_IDTOKEN;

          Array.Copy(buffer, index, TokenAnchor.HashBlockReferenced, 0, TokenAnchor.HashBlockReferenced.Length);
          index += TokenAnchor.HashBlockReferenced.Length;

          Array.Copy(buffer, index, TokenAnchor.HashBlockPreviousReferenced, 0, TokenAnchor.HashBlockPreviousReferenced.Length);
          index += TokenAnchor.HashBlockPreviousReferenced.Length;
        }
      }

      public List<(string label, string value)> GetLabelsValuePairs()
      {
        List<(string label, string value)> labelValuePairs = new();

        labelValuePairs.Add(($"IDToken", $"{TokenAnchor.IDToken.ToHexString()}"));
        labelValuePairs.Add(($"HashBlockReferenced", $"{TokenAnchor.HashBlockReferenced.ToHexString()}"));
        labelValuePairs.Add(($"HashBlockPreviousReferenced", $"{TokenAnchor.HashBlockPreviousReferenced.ToHexString()}"));

        return labelValuePairs;
      }

    }
  }
}
