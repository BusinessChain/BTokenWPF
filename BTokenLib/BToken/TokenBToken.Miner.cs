using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BTokenLib
{
  partial class TokenBToken : Token
  {
    const int COUNT_TXS_PER_BLOCK_MAX = 5;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 10;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 10;
    const double FACTOR_INCREMENT_FEE_PER_BYTE = 1.2;

    string PathBlocksMinedUnconfirmed;
    List<BlockBToken> BlocksMined = new();

    SHA256 SHA256Miner = SHA256.Create();
    Random RandomGeneratorMiner = new();
    int NumberSequence;

    List<TokenAnchor> TokensAnchorSelfMinedUnconfirmed = new();
    List<TokenAnchor> TokensAnchorDetectedInBlock = new();


    protected override async void RunMining()
    {
      $"Miner {this} starts.".Log(this, LogFile, LogEntryNotifier);

      Header headerTipParent = null;
      Header headerTip = null;

      int timerMinerPause = 0;
      int timerCreateNextToken = 0;
      int timeMinerLoopMilliseconds = 100;

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

            TokenAnchor tokenAnchor = MineAnchorToken();

            if (TokenParent.TryBroadcastAnchorToken(tokenAnchor))
            {
              TokensAnchorSelfMinedUnconfirmed.Add(tokenAnchor);

              $"{TokensAnchorSelfMinedUnconfirmed.Count} mined unconfirmed anchor tokens referencing block {tokenAnchor.HashBlockReferenced.ToHexString()}.".Log(this, LogFile, LogEntryNotifier);

              // timeMSLoop = (int)(tokenAnchor.TX.Fee * TIMESPAN_DAY_SECONDS * 1000 /
              // COUNT_SATOSHIS_PER_DAY_MINING);

              // timeMSLoop = RandomGeneratorMiner.Next(
              // timeMSCreateNextAnchorToken / 2,
              // timeMSCreateNextAnchorToken * 3 / 2);
            }
            else
              IsMining = false;
          }

          ReleaseLock();
        }

        await Task.Delay(timeMinerLoopMilliseconds).ConfigureAwait(false);
      }

      $"Exit BToken miner.".Log(this, LogFile, LogEntryNotifier);
    }

    public TokenAnchor MineAnchorToken()
    {
      BlockBToken block = new(this);

      block.TXs.AddRange(TXPool.GetTXs(COUNT_TXS_PER_BLOCK_MAX)); // should be bytes per block

      int height = HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      blockReward += block.TXs.Sum(t => t.Fee);

      TX tXCoinbase = ((WalletBToken)Wallet).CreateCoinbaseTX(height, blockReward);

      block.TXs.Insert(0, tXCoinbase);

      block.Header = new HeaderBToken()
      {
        HashPrevious = HeaderTip.Hash,
        HeaderPrevious = HeaderTip,
        Height = height,
        UnixTimeSeconds = (uint)DateTimeOffset.Now.ToUnixTimeSeconds(),
        MerkleRoot = block.ComputeMerkleRoot(),
        CountTXs = block.TXs.Count
      };

      block.Header.ComputeHash(SHA256Miner);

      TokenAnchor tokenAnchor = new();

      tokenAnchor.HashBlockReferenced = block.Header.Hash;
      tokenAnchor.HashBlockPreviousReferenced = block.Header.HashPrevious;
      tokenAnchor.IDToken = IDToken;

      string pathFileBlock = Path.Combine(PathBlocksMinedUnconfirmed, block.ToString());

      // File.WriteAllBytes(pathFileBlock, block.Buffer);

      BlocksMined.Add(block);

      $"BToken minSignalAnchorTokenDetecteder successfully mined anchor Token {tokenAnchor}.".Log(this, LogFile, LogEntryNotifier);

      return tokenAnchor;
    }

    public override void SignalParentBlockInsertion(Header headerAnchor)
    {
      Block block;

      try
      {
        if (TokensAnchorDetectedInBlock.Count > 0)
        {
          headerAnchor.HashChild = GetHashBlockChildWinner(headerAnchor.Hash);

          TokensAnchorDetectedInBlock.Clear();

          if (BlocksMined.Count > 0)
          {
            block = BlocksMined.Find(b =>
            b.Header.Hash.IsEqual(headerAnchor.HashChild));

            BlocksMined.Clear();

            if (block == null)
              return;

            if (block.Header.HashPrevious.IsEqual(HeaderTip.Hash))
              InsertBlock(block);
            else
              ($"Self mined block {block} is obsoleted.\n" +
                $"This may happen, if a miner in the anchor chain withholds a mined block,\n" +
                $"and releases it after mining a subsequent block which now contains anchor " +
                $"tokens referencing a block prior to the tip.").Log(this, LogFile, LogEntryNotifier);
          }
        }
      }
      catch (Exception ex)
      {
        block = null;

        ($"{ex.GetType().Name} when signaling Bitcoin block {headerAnchor}" +
          $" with height {headerAnchor.Height} to BToken:\n" +
          $"Exception message: {ex.Message}").Log(this, LogFile, LogEntryNotifier);
      }

      if (TokensAnchorSelfMinedUnconfirmed.Count > 0)
      {
        $"RBF {TokensAnchorSelfMinedUnconfirmed.Count} anchor tokens.".Log(this, LogFile, LogEntryNotifier);

        TokensAnchorSelfMinedUnconfirmed.ForEach(t => File.Delete(Path.Combine(
            PathBlocksMinedUnconfirmed,
            t.TX.Hash.ToHexString())));

        TokenAnchor tokenAnchorNew = MineAnchorToken();
        tokenAnchorNew.NumberSequence = TokensAnchorSelfMinedUnconfirmed[0].NumberSequence += 1;
        
        TokenParent.RBFAnchorTokens(ref TokensAnchorSelfMinedUnconfirmed, tokenAnchorNew);
      }
    }

    public override void SignalAnchorTokenDetected(TokenAnchor tokenAnchor)
    {
      if (TokensAnchorSelfMinedUnconfirmed.RemoveAll(t => t.TX.Hash.IsEqual(tokenAnchor.TX.Hash)) > 0)
        $"Detected self mined anchor token {tokenAnchor} in Bitcoin block.".Log(this, LogFile, LogEntryNotifier);
      else
        $"Detected foreign mined anchor token {tokenAnchor} in Bitcoin block.".Log(this, LogFile, LogEntryNotifier);

      TokensAnchorDetectedInBlock.Add(tokenAnchor);
    }

    public override void RBFAnchorTokens(
      ref List<TokenAnchor> tokensAnchorSelfMinedUnconfirmed,
      TokenAnchor tokenAnchorTemplate)
    {
      throw new NotImplementedException();
    }

    byte[] GetHashBlockChildWinner(byte[] hashHeaderAnchor)
    {
      SHA256 sHA256 = SHA256.Create();

      byte[] targetValue = sHA256.ComputeHash(hashHeaderAnchor);
      byte[] biggestDifferenceTemp = new byte[32];
      TokenAnchor tokenAnchorWinner = null;

      TokensAnchorDetectedInBlock.ForEach(t =>
      {
        byte[] differenceHash = targetValue.SubtractByteWise(
          t.HashBlockReferenced);

        if (differenceHash.IsGreaterThan(biggestDifferenceTemp))
        {
          biggestDifferenceTemp = differenceHash;
          tokenAnchorWinner = t;
        }
      });

      ($"The winning anchor token is {tokenAnchorWinner} referencing block " +
        $"{tokenAnchorWinner.HashBlockReferenced.ToHexString()}.").Log(this, LogFile, LogEntryNotifier);

      return tokenAnchorWinner.HashBlockReferenced;
    }
  }
}
