using System;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    class TXOutputBToken : TXOutput
    {
      public byte[] IDAccount;

      public byte[] Data;

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
      }
    }
  }
}
