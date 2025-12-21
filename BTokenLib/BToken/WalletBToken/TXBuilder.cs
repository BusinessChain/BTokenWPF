using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public partial class WalletBToken : Wallet
    {
      abstract class TXBuilder
      {
        public byte[] KeyPublicSource;

        public double FeePerByte;
        public long Fee;

        public TXBuilder(byte[] keyPublicSource, double feePerByte)
        {
          KeyPublicSource = keyPublicSource;
          FeePerByte = feePerByte;
        }

        public abstract void CheckFee(long fundsAccount);

        public abstract byte[] CreateTXRaw(Wallet wallet, int blockHeightAccountCreated, int nonce);
      }

      class TXValueBuilder : TXBuilder
      {
        public string AddressDest;
        public long Value;

        public TXValueBuilder(byte[] keyPublicSource, string addressDest, long value, double feePerByte)
          : base(keyPublicSource, feePerByte)
        {
          AddressDest = addressDest;
          Value = value;
        }

        public override void CheckFee(long fundsAccount)
        {
          Fee = (long)(FeePerByte * LENGTH_TX_P2PKH);

          if (fundsAccount < Value + Fee)
            throw new ProtocolException(
              $"Not enough funds, balance {fundsAccount} sats " +
              $"smaller than tX output value {Value} plus fee {Fee} totaling {Value + Fee}.");
        }

        public override byte[] CreateTXRaw(Wallet wallet, int blockHeightAccountCreated, int nonce)
        {
          List<byte> tXRaw = new();

          tXRaw.Add((byte)TypesToken.ValueTransfer);
          tXRaw.AddRange(KeyPublicSource);
          tXRaw.AddRange(BitConverter.GetBytes(blockHeightAccountCreated));
          tXRaw.AddRange(BitConverter.GetBytes(nonce));
          tXRaw.AddRange(BitConverter.GetBytes(Fee));
          tXRaw.Add(0x01); // count outputs
          tXRaw.AddRange(BitConverter.GetBytes(Value));
          tXRaw.AddRange(AddressDest.Base58CheckToPubKeyHash());

          byte[] signature = wallet.GetSignature(tXRaw.ToArray());

          tXRaw.Add((byte)signature.Length);
          tXRaw.AddRange(signature);

          return tXRaw.ToArray();
        }
      }

      class TXDataBuilder : TXBuilder
      {
        public byte[] Data;

        public TXDataBuilder(byte[] keyPublicSource, byte[] data, double feePerByte)
          : base(keyPublicSource, feePerByte)
        {
          Data = data;
        }

        public override void CheckFee(long fundsAccount)
        {
          long fee = (long)(FeePerByte * (LENGTH_TX_DATA_SCAFFOLD + Data.Length));

          if (fundsAccount < fee)
            throw new ProtocolException($"Not enough funds, balance {fundsAccount} sats fee {fee}.");
        }

        public override byte[] CreateTXRaw(Wallet wallet, int blockHeightAccountCreated, int nonce)
        {
          List<byte> tXRaw = new();

          tXRaw.Add((byte)TypesToken.Data);
          tXRaw.AddRange(KeyPublicSource);
          tXRaw.AddRange(BitConverter.GetBytes(blockHeightAccountCreated));
          tXRaw.AddRange(BitConverter.GetBytes(nonce));
          tXRaw.AddRange(BitConverter.GetBytes(Fee));
          tXRaw.Add(0x01);
          tXRaw.AddRange(VarInt.GetBytes(Data.Length));
          tXRaw.AddRange(Data);

          byte[] signature = wallet.GetSignature(tXRaw.ToArray());

          tXRaw.Add((byte)signature.Length);
          tXRaw.AddRange(signature);

          return tXRaw.ToArray();
        }
      }
    }
  }
}
