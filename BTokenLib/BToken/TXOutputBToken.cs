using System;


namespace BTokenLib
{
  public class TXOutputBToken
  {
    public byte[] IDAccount;
    public long Value;


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
