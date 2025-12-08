using System;
using System.Linq;
using System.Collections.Generic;

using LiteDB;
using System.Threading.Tasks;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public partial class WalletBToken : Wallet
    {
      TokenBToken Token;

      Account AccountWalletConfirmed;
      Account AccountWalletUnconfirmed;
      int SerialNumberTX;

      LiteDatabase Database;
      ILiteCollection<DBRecordTXWallet> DatabaseTXCollection;

      static int LENGTH_P2PKH_TX = 120;


      public WalletBToken(string privKeyDec, TokenBToken token)
        : base(privKeyDec)
      {
        Token = token;

        DatabaseTXCollection = Database.GetCollection<DBRecordTXWallet>("RecordsTXWallet");
      }

      public TX CreateTXCoinbase(long blockReward, int blockHeight)
      {
        List<byte> tXRaw = new()
      {
        (byte)TokenBToken.TypesToken.Coinbase
      };

        tXRaw.AddRange(BitConverter.GetBytes(blockHeight));

        tXRaw.Add(0x01); // count outputs

        tXRaw.AddRange(BitConverter.GetBytes(blockReward));
        tXRaw.AddRange(PublicKeyHash160);

        return Token.ParseTX(tXRaw.ToArray(), SHA256);
      }


      List<(string address, long value, double feePerByte)> TXsQueueSend = new();

      // Die Wallet muss selber einen Account mitführen, den sie als aktuell gültig betrachtet. 
      // Zum Beispiel immer beim Versuch eine erfasste TX zu senden oder, wenn ein neue konfirmierte TX ankommt
      // wird die konsistenz zur Token DB geprüft.

      // Die Wallet über ein Interface an das Token ankoppeln.

      List<TX> TXsPending = new();

      public override bool TrySendTXValue(string address, long value, double feePerByte)
      {
        // überprüfen, ob gemäss TXs pending (Wallet) und TXs nonconfirmed (Pool) die TX gültig ist (genug Funds).
        // Allenfalls muss hier das Token gefragt werden, wieviele bytes eine TX benötigt um (fee) zu berechnen.

        long fee = (long)(feePerByte * LENGTH_P2PKH_TX);


        byte[] tXRawToBeSigned = Token.CreateTXRaw(address, value, fee);

        byte[] signature = GetSignature(tXRawToBeSigned.ToArray());

        TX tX = Token.CreateTX(tXRawToBeSigned, signature);

        TXsPending.Add(tX);

        Token.SendTX(tX);

        return true;
      }

      public override bool TrySendTXData(byte[] data, double feePerByte)
      {
        return Token.TrySendTXData(data, feePerByte, AccountWalletUnconfirmed);
      }

      public override void InsertTX(TX tX, int heightBlock)
      {
        TXBToken tXBToken = tX as TXBToken;

        bool flagIndexTX = false;

        if (tXBToken.IDAccountSource.IsAllBytesEqual(PublicKeyHash160))
          flagIndexTX = true;

        foreach (TXOutputBToken output in tXBToken.TXOutputs)
          if (output.IDAccount.IsAllBytesEqual(PublicKeyHash160))
            flagIndexTX = true;

        if(flagIndexTX)
        {
          DBRecordTXWallet record = new ()
          {
            HashTX = tX.Hash,
            BlockHeightOriginTX = heightBlock,
            SerialNumberTX = SerialNumberTX++,
            TXRaw = tX.TXRaw
          };
          DatabaseTXCollection.Upsert(record);
        }
      }

      public override void ReverseBlock(Block block)
      {

      }

      public override long GetBalance()
      {
        try
        {
          Account accountUnconfirmed = Token.GetAccountUnconfirmed(PublicKeyHash160);
          return accountUnconfirmed.Balance;
        }
        catch
        {
          return 0;
        }
      }
    }
  }
}