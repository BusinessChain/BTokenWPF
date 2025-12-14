using System;
using System.Linq;
using System.Collections.Generic;

using LiteDB;


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
        (byte)TypesToken.Coinbase
      };

        tXRaw.AddRange(BitConverter.GetBytes(blockHeight));

        tXRaw.Add(0x01); // count outputs

        tXRaw.AddRange(BitConverter.GetBytes(blockReward));
        tXRaw.AddRange(Hash160PKeyPublic);

        return Token.ParseTX(tXRaw.ToArray(), SHA256);
      }


      List<(string address, long value, double feePerByte)> TXsQueueSend = new();

      // Die Wallet muss selber einen Account mitführen, den sie als aktuell gültig betrachtet. 
      // Zum Beispiel immer beim Versuch eine erfasste TX zu senden oder, wenn ein neue konfirmierte TX ankommt
      // wird die konsistenz zur Token DB geprüft.

      // Die Wallet über ein Interface an das Token ankoppeln.

      List<TX> TXsPending = new();

      public override void SendTXValue(string addressDest, long value, double feePerByte, out string errorMessage)
      {
        // überprüfen, ob gemäss TXs pending (Wallet) und TXs nonconfirmed (Pool) die TX gültig ist (genug Funds).
        // Allenfalls muss hier das Token gefragt werden, wieviele bytes eine TX benötigt um (fee) zu berechnen.

        List<byte> tXRaw = Token.CreateTXValueRaw(KeyPublic, addressDest, value, feePerByte);

        byte[] signature = GetSignature(tXRaw.ToArray());

        TX tX = Token.CreateTXValue(tXRaw, signature);

        // Fire and Forget.
        // Keine Garantie, dass die TX tatsächlich im Netzwerk aufgenommen wird.
        // Deshalb geht die abgeschickte TX in die TXsPending rein und wird dann bei Bedarf erneut versendet.
        Token.BroadcastTX(tX);
        
        TXsPending.Add(tX);
      }

      public override bool TrySendTXData(byte[] data, double feePerByte)
      {
        return Token.TrySendTXData(data, feePerByte, AccountWalletUnconfirmed);
      }

      public override void InsertTX(TX tX, int heightBlock)
      {
        TXBToken tXBToken = tX as TXBToken;

        bool flagIndexTX = false;

        if (tXBToken.IDAccountSource.IsAllBytesEqual(Hash160PKeyPublic))
          flagIndexTX = true;

        foreach (TXOutputBToken output in tXBToken.TXOutputs)
          if (output.IDAccount.IsAllBytesEqual(Hash160PKeyPublic))
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
          Account accountUnconfirmed = Token.GetAccountUnconfirmed(Hash160PKeyPublic);
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