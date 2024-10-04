using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    const int COUNT_BYTES_PER_BLOCK_MAX = 1000;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 5;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 10;
    const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
    const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;

    double FeeSatoshiPerByteAnchorToken;
    List<TX> TokensAnchorMinedUnconfirmed = new();
    List<Block> BlocksMinedCache = new();
    string PathBlocksMined;


    protected override async void RunMining()
    {
      $"Miner {this} starts.".Log(this, LogFile, LogEntryNotifier);

      Header headerTipParent = null;
      Header headerTip = null;

      int timerMinerPause = 0;
      int timerCreateNextToken = 0;
      int timeMinerLoopMilliseconds = 100;

      FeeSatoshiPerByteAnchorToken = TokenParent.HeaderTip.FeePerByte;

      if (FeeSatoshiPerByteAnchorToken < MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN)
        FeeSatoshiPerByteAnchorToken = MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN;

      while (IsMining)
      {
        if (TryLock())
        {
          if (headerTip == null)
          {
            headerTip = HeaderTip;
            headerTipParent = TokenParent.HeaderTip;
          }

          if (headerTipParent != TokenParent.HeaderTip)
          {
            timerMinerPause = TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS * 1000
              / timeMinerLoopMilliseconds;

            headerTipParent = TokenParent.HeaderTip;
          }

          if (timerMinerPause > 0)
            timerMinerPause -= 1;

          if (timerCreateNextToken > 0)
            timerCreateNextToken -= 1;

          if (timerMinerPause == 0 && timerCreateNextToken == 0)
          {
            timerCreateNextToken = TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS * 1000
              / timeMinerLoopMilliseconds;

            byte[] dataAnchorToken = CreateAnchorToken(out Block block);

            if (TokenParent.TryBroadcastTXData(dataAnchorToken, FeeSatoshiPerByteAnchorToken))
            {
              $"Mine block {block}.".Log(this, LogFile, LogEntryNotifier);

              SaveBlockMined(block);

              // timeMSLoop = (int)(tokenAnchor.TX.Fee * TIMESPAN_DAY_SECONDS * 1000 /
              // COUNT_SATOSHIS_PER_DAY_MINING);

              // timeMSLoop = RandomGeneratorMiner.Next(
              // timeMSCreateNextAnchorToken / 2,
              // timeMSCreateNextAnchorToken * 3 / 2);
            }
          }

          ReleaseLock();
        }

        await Task.Delay(timeMinerLoopMilliseconds).ConfigureAwait(false);
      }

      $"Exit BToken miner.".Log(this, LogFile, LogEntryNotifier);
    }

    byte[] CreateAnchorToken(out Block block)
    {
      block = new BlockBToken(this);

      block.TXs.AddRange(TXPool.GetTXs(COUNT_BYTES_PER_BLOCK_MAX, out long feeTXs));

      int height = HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      blockReward += feeTXs;

      TXBToken tXCoinbase = ((WalletBToken)Wallet).CreateTXCoinbase(blockReward, height);

      block.TXs.Insert(0, tXCoinbase);

      block.Header = new HeaderBToken()
      {
        HashPrevious = HeaderTip.Hash,
        HeaderPrevious = HeaderTip,
        Height = height,
        UnixTimeSeconds = (uint)DateTimeOffset.Now.ToUnixTimeSeconds(),
        MerkleRoot = block.ComputeMerkleRoot(),
        CountTXs = block.TXs.Count,
        Fee = feeTXs
      };

      block.Header.ComputeHash();

      return IDENTIFIER_BTOKEN_PROTOCOL
      .Concat(IDToken)
      .Concat(block.Header.Hash)
      .Concat(block.Header.HashPrevious).ToArray();
    }

    void SaveBlockMined(Block block)
    {
      BlocksMinedCache.Add(block);

      string pathBlockMined = Path.Combine(PathBlocksMined, block.Header.Hash.ToHexString());

      while (true)
        try
        {
          using (FileStream fileStreamBlock = new(
          pathBlockMined,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
          {
            block.Serialize(fileStreamBlock);
          }

          return;
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when writing block height {block.Header.Height} to file:\n" +
            $"{ex.Message}\n " +
            $"Try again in {TIMEOUT_FILE_RELOAD_SECONDS} seconds ...").Log(this, LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS);
        }
    }

    public override void SignalParentBlockInsertion()
    {
      if (TokensAnchorMinedUnconfirmed.Count > 0)
      {
        TX tXTokenAnchorOld = TokensAnchorMinedUnconfirmed.Last();

        FeeSatoshiPerByteAnchorToken *= FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN;

        byte[] dataAnchorTokenNew = CreateAnchorToken(out Block block);

        if (TokenParent.TryRBFTXData(tXTokenAnchorOld, dataAnchorTokenNew, FeeSatoshiPerByteAnchorToken))
        {
          $"RBF old anchor token {tXTokenAnchorOld} with new anchor token referenching {block}.".Log(this, LogFile, LogEntryNotifier);

          SaveBlockMined(block);
        }
      }
    }

    public override void SignalHashBlockWinnerToChild(byte[] hashBlockChildToken)
    {
      if (TryGetBlockMined(hashBlockChildToken, out Block block))
      {
        $"Insert self mined block {block}.".Log(this, LogFile, LogEntryNotifier);

        try
        {
          InsertBlock(block);
          Network.AdvertizeBlockToNetwork(block);
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when inserting self mined block {block}:\n" +
            $"{ex.Message}").Log(this, LogFile, LogEntryNotifier);
        }
      }
    }

    public override void DeleteBlocksMinedUnconfirmed()
    {
      BlocksMinedCache.Clear();

      foreach (string pathFile in Directory.GetFiles(PathBlocksMined))
        File.Delete(pathFile);
    }

    public override void ReceiveAnchorTokenConfirmed(TX tX)
    {
      TokensAnchorMinedUnconfirmed.RemoveAll(t => t.Hash.IsAllBytesEqual(tX.Hash));
    }

    public override void SaveAnchorTokenUnconfirmedMined(TX tXTokenAnchor)
    {
      TX tXTokenAnchorRBFed = TokensAnchorMinedUnconfirmed.Find(t => tXTokenAnchor.IsReplacementByFeeFor(t));

      if (TokensAnchorMinedUnconfirmed.Remove(tXTokenAnchorRBFed))
        $"Removed RBF'ed and unconfirmed anchor token {tXTokenAnchorRBFed} from list TokensAnchorMinedUnconfirmed."
          .Log(this, LogFile, LogEntryNotifier);

      TokensAnchorMinedUnconfirmed.Add(tXTokenAnchor);

      $"Included anchor token {tXTokenAnchor}. {TokensAnchorMinedUnconfirmed.Count} anchor tokens in TokensAnchorMinedUnconfirmed."
        .Log(this, LogFile, LogEntryNotifier);
    }

    bool TryGetBlockMined(byte[] hashBlock, out Block blockMined)
    {
      blockMined = BlocksMinedCache.Find(b => b.Header.Hash.IsAllBytesEqual(hashBlock));

      if (blockMined != null)
        return true;

      string pathBlockMined = Path.Combine(PathBlocksMined, hashBlock.ToHexString());

      while (true)
        try
        {
          using (FileStream fileStream = new(
            pathBlockMined,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
          {
            try
            {
              blockMined = ParseBlock(fileStream);
              return true;
            }
            catch (Exception ex)
            {
              $"{ex.GetType().Name} when attempting to parse file {pathBlockMined}: {ex.Message}"
                .Log(this, LogEntryNotifier);

              break;
            }
          }
        }
        catch (FileNotFoundException)
        {
          break;
        }
        catch (IOException ex)
        {
          ($"{ex.GetType().Name} when attempting to load file {pathBlockMined}: {ex.Message}.\n" +
            $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
          continue;
        }

      return false;
    }
  }
}
