using System;


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

      public byte[] IDAccount;
      public long Value;

      public byte[] Data;

      public byte[] Script;


      public TXOutputBToken() 
      { }

      public TXOutputBToken(byte[] buffer, ref int index)
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
