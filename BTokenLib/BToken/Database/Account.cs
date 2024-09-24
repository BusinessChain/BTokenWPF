namespace BTokenLib
{
  public class Account
  {
    public byte[] IDAccount;
    public int BlockHeightAccountInit;
    public int Nonce;
    public long Value;


    public override string ToString()
    {
      return IDAccount.ToHexString();
    }
  }
}