namespace BTokenLib
{
  class Account
  {
    public ulong Nonce; 
    // was ist wenn ein Account value null wird und aus der DB gelöscht wird?
    // wenn später der account wieder geöffnet wird muss sicher sein dass nicht wieder dieselben nonces gebraucht werden
    // deshalb muss in der nonce noch die blockheight der account eröffnung drin stehen, also besser ulong
    public long Value;
    public byte[] IDAccount;


    public bool CheckTXValid(TXBToken tX)
    {
      if (Value < tX.Value)
        throw new ProtocolException($"Value {Value} on account {this}" +
          $"is lower than value {tX.Value} of tX {tX}.");

      if (Nonce < tX.Nonce)
        throw new ProtocolException($"Nonce {Nonce} on account {this}" +
          $"is lower than value {tX.Nonce} of tX {tX}.");

      return true;
    }

    public override string ToString()
    {
      return IDAccount.ToHexString();
    }
  }
}