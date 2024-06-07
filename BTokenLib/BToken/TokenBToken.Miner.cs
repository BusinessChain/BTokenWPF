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
    const int COUNT_BYTES_PER_BLOCK_MAX = 4000000;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 10;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 10;
    const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
    const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;

    SHA256 SHA256Miner = SHA256.Create();
    Random RandomGeneratorMiner = new();

    double FeeSatoshiPerByteAnchorToken;

    AnchorTokenConsensusAlgorithm AnchorTokenConsensusAlgorithm;


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

            TokenAnchor tokenAnchor = MineAnchorToken(numberSequence: 0);

            if (TokenParent.TryBroadcastAnchorToken(tokenAnchor))
            {
              AnchorTokenConsensusAlgorithm.IncludeAnchorTokenMined(tokenAnchor);

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

    public TokenAnchor MineAnchorToken(int numberSequence)
    {
      BlockBToken block = new(this);

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
        CountTXs = block.TXs.Count
      };

      block.Header.ComputeHash(SHA256Miner);

      TokenAnchor tokenAnchor = new();

      tokenAnchor.BlockAnchored = block;
      tokenAnchor.HashBlockReferenced = block.Header.Hash;
      tokenAnchor.HashBlockPreviousReferenced = block.Header.HashPrevious;
      tokenAnchor.IDToken = IDToken;
      tokenAnchor.FeeSatoshiPerByte = FeeSatoshiPerByteAnchorToken;
      tokenAnchor.NumberSequence = numberSequence;

      return tokenAnchor;
    }


    public override void SignalParentBlockInsertion(Header headerAnchor)
    {
      if (AnchorTokenConsensusAlgorithm.TryGetAnchorTokenWinner(
        headerAnchor.Hash, 
        out TokenAnchor tokenAnchorWinner))
      {
        try
        {
          ($"The winning anchor token is {tokenAnchorWinner.TX} referencing block " +
            $"{tokenAnchorWinner.BlockAnchored}.").Log(this, LogFile, LogEntryNotifier);

          InsertBlock(tokenAnchorWinner.BlockAnchored);
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when inserting anchored block {tokenAnchorWinner.BlockAnchored}:\n" +
            $"{ex.Message}").Log(this, LogFile, LogEntryNotifier);
        }
      }
      
      if(AnchorTokenConsensusAlgorithm.TryGetAnchorTokenRBF(out TokenAnchor tokenAnchorOld))
      {
        $"RBF anchor token {tokenAnchorOld}.".Log(this, LogFile, LogEntryNotifier);

        FeeSatoshiPerByteAnchorToken *= FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN;

        TokenAnchor tokenAnchorNew = MineAnchorToken(tokenAnchorOld.NumberSequence + 1);

        if(TokenParent.TryRBFAnchorToken(tokenAnchorOld, tokenAnchorNew))
          AnchorTokenConsensusAlgorithm.IncludeAnchorTokenMined(tokenAnchorNew);
      }
    }

    public override void SignalAnchorTokenDetected(TokenAnchor tokenAnchor)
    {
      AnchorTokenConsensusAlgorithm.IncludeAnchorTokenConfirmed(
        tokenAnchor, 
        out bool flagTokenAnchorWasSelfMined);

      if(flagTokenAnchorWasSelfMined)
      {
        $"Detected self mined anchor token {tokenAnchor} in Bitcoin block.".Log(this, LogFile, LogEntryNotifier);
        if (FeeSatoshiPerByteAnchorToken > tokenAnchor.FeeSatoshiPerByte)
        {
          FeeSatoshiPerByteAnchorToken /= FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN;

          if (FeeSatoshiPerByteAnchorToken < MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN)
            FeeSatoshiPerByteAnchorToken = MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN;
        }
      }
      else
      {
        $"Detected foreign mined anchor token {tokenAnchor} in Bitcoin block.".Log(this, LogFile, LogEntryNotifier);
      }
    }
  }
}
