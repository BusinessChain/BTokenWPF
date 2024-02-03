using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  public class TXOutputBToken : TXOutput
  {
    public byte[] IDAccount;
    public TypesToken Type;


    public enum TypesToken
    {
      ValueTransfer = 0x00
    }

    public TXOutputBToken(byte[] buffer, int startIndex)
    {
      Buffer = buffer; 
      StartIndexScript = startIndex;

      Type = (TypesToken)buffer[startIndex];

      if (Type == TypesToken.ValueTransfer)
      {
        Value = BitConverter.ToInt64(buffer, startIndex);

        startIndex += 8;

        IDAccount = new byte[TXBToken.LENGTH_IDACCOUNT];

        Array.Copy(
          buffer,
          startIndex,
          IDAccount,
          startIndex,
          TXBToken.LENGTH_IDACCOUNT);
      }
    }
  }
}
