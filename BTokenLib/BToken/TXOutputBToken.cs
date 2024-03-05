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
      ValueTransfer = 0x00,
      Data = 0x01
    }

    public TXOutputBToken(byte[] buffer, ref int index)
    {
      Buffer = buffer; 
      StartIndexScript = index;

      Type = (TypesToken)buffer[index++];

      if (Type == TypesToken.ValueTransfer)
      {
        Value = BitConverter.ToInt64(buffer, index);

        index += 8;

        IDAccount = new byte[TXBToken.LENGTH_IDACCOUNT];

        Array.Copy(buffer, index, IDAccount, index, TXBToken.LENGTH_IDACCOUNT);
        index += TXBToken.LENGTH_IDACCOUNT;
      }
      else if (Type == TypesToken.Data)
      {

      }
    }
  }
}
