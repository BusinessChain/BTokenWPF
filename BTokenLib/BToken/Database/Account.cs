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

    public void SpendTX(TXBToken tX)
    {
      if (Nonce != tX.Nonce)
        throw new ProtocolException($"Account {this} referenced by TX {tX} has unequal Nonce.");

      if (Value < tX.Value)
        throw new ProtocolException($"Account {this} referenced by TX {tX} does not have enough fund.");

      Nonce += 1;
      Value -= tX.Value;
    }
  }
}