namespace BTokenLib
{
  class Account
  {
    public long Nonce; 
    // blockheight mit (Nonce & 0x0000ffff)  rausfiltern
    public long Value;
    public byte[] IDAccount;


    public bool CheckTXValid(TXBToken tX)
    {
      if (Value < tX.Value)
        throw new ProtocolException($"Value {Value} on account {this}" +
          $"is lower than value {tX.Value} of tX {tX}.");
      tX.ValueInDB = Value;

      if (Nonce < tX.Nonce)
        throw new ProtocolException($"Nonce {Nonce} on account {this}" +
          $"is lower than value {tX.Nonce} of tX {tX}.");
      tX.NonceInDB = Nonce;

      return true;
    }

    public override string ToString()
    {
      return IDAccount.ToHexString();
    }
  }
}