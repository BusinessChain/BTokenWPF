using System;
using System.Collections.Generic;

namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public partial class WalletBToken : Wallet
    {
      abstract class BuilderTXBToken
      {
        public byte[] TXRaw;
      }

      class BuilderTXBTokenValue : BuilderTXBToken
      {
        const int LENGTH_TX_P2PKH = 120;

        public BuilderTXBTokenValue(Wallet wallet, byte[] keyPublicSource, Account accountSource, string addressDest, long value, double feePerByte)
        {
          long fee = (long)(feePerByte * LENGTH_TX_P2PKH);

          if (accountSource.Balance < value + fee)
            throw new ProtocolException(
              $"Not enough funds: balance {accountSource.Balance} " +
              $"less than tX output value {value} plus fee {fee} totaling {value + fee}.");

          List<byte> tXRaw = new();

          tXRaw.Add((byte)TypesToken.ValueTransfer);
          tXRaw.AddRange(keyPublicSource);
          tXRaw.AddRange(BitConverter.GetBytes(accountSource.BlockHeightAccountCreated));
          tXRaw.AddRange(BitConverter.GetBytes(accountSource.Nonce));
          tXRaw.AddRange(BitConverter.GetBytes(fee));
          tXRaw.Add(0x01); // count outputs
          tXRaw.AddRange(BitConverter.GetBytes(value));
          tXRaw.AddRange(addressDest.Base58CheckToPubKeyHash());

          byte[] signature = wallet.GetSignature(tXRaw.ToArray());

          tXRaw.Add((byte)signature.Length);
          tXRaw.AddRange(signature);

          TXRaw = tXRaw.ToArray();
        }
      }

      class BuilderTXBTokenData : BuilderTXBToken
      {
        const int LENGTH_TX_DATA_SCAFFOLD = 30;


        public BuilderTXBTokenData(Wallet wallet, byte[] keyPublicSource, Account accountSource, byte[] data, double feePerByte)
        {
          long fee = (long)(feePerByte * (LENGTH_TX_DATA_SCAFFOLD + data.Length));

          if (accountSource.Balance < fee)
            throw new ProtocolException($"Not enough funds, balance {accountSource.Balance} less than fee {fee}.");

          List<byte> tXRaw = new();

          tXRaw.Add((byte)TypesToken.Data);
          tXRaw.AddRange(keyPublicSource);
          tXRaw.AddRange(BitConverter.GetBytes(accountSource.BlockHeightAccountCreated));
          tXRaw.AddRange(BitConverter.GetBytes(accountSource.Nonce));
          tXRaw.AddRange(BitConverter.GetBytes(fee));
          tXRaw.Add(0x01);
          tXRaw.AddRange(VarInt.GetBytes(data.Length));
          tXRaw.AddRange(data);

          byte[] signature = wallet.GetSignature(tXRaw.ToArray());

          tXRaw.Add((byte)signature.Length);
          tXRaw.AddRange(signature);

          TXRaw = tXRaw.ToArray();
        }
      }
    }
  }
}
