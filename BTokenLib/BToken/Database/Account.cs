namespace BTokenLib
{
  public class Account
  {
    public int BlockheightAccountInit;
    public int Nonce;
    public long Value;
    public byte[] IDAccount;


    public override string ToString()
    {
      return IDAccount.ToHexString();
    }
  }
}