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
      Account AccountWalletConfirmed;
      Account AccountWalletUnconfirmed;
      int SerialNumberTX;

      List<TX> TXsUnconfirmedReceived = new();

      LiteDatabase Database;
      ILiteCollection<DBRecordTXWallet> DatabaseTXCollection;

      public const byte OP_RETURN = 0x6A;


      public WalletBToken(string privKeyDec, TokenBToken token)
        : base(privKeyDec)
      {
        Token = token;

        DatabaseTXCollection = Database.GetCollection<DBRecordTXWallet>("RecordsTXWallet");
      }

      const int LENGTH_TX_P2PKH = 120;

      public override void InsertBlock(Block block)
      {
        foreach (TXBToken tX in block.TXs)
        {
          bool flagIndexTX = false;

          if (tX.IDAccountSource.IsAllBytesEqual(Hash160PKeyPublic))
          {
            flagIndexTX = true;
            AccountWalletConfirmed.SpendTX(tX);
          }

          foreach (TXOutputP2PKH output in tX.TXOutputs)
            if (output.IDAccount.IsAllBytesEqual(Hash160PKeyPublic))
            {
              flagIndexTX = true;
              AccountWalletConfirmed.Balance += output.Value;
            }

          if (flagIndexTX)
            DatabaseTXCollection.Upsert(
              new DBRecordTXWallet()
              {
                HashTX = tX.Hash,
                BlockHeightOriginTX = block.Header.Height,
                SerialNumberTX = SerialNumberTX++,
                TXRaw = tX.TXRaw
              });
        }
      }

      public override void ReverseBlock(Block block)
      {

      }
          
      public override long GetBalance()
      {
        return AccountWalletUnconfirmed.Balance;
      }
    }
  }
}