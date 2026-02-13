using System;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  partial class Network
  {
    partial class Synchronization
    {
      int HeightTipQueueBlocks;
      double DifficultyAccumulatedHeightTip;
      Dictionary<int, Block> QueueBlocks = new();

      bool TryAddBlockToQueueBlocks(int heightBlock, Block block)
      {
        if (heightBlock <= HeightTipQueueBlocks || !QueueBlocks.TryAdd(heightBlock, block))
          return false;

        if (HeightTipQueueBlocks == 0)
        {
          HeightTipQueueBlocks = heightBlock;
          DifficultyAccumulatedHeightTip += block.Header.DifficultyAccumulated;
        }
        else if (heightBlock == HeightTipQueueBlocks + 1)
          do
          {
            HeightTipQueueBlocks++;
            DifficultyAccumulatedHeightTip += block.Header.DifficultyAccumulated;
          }
          while (QueueBlocks.TryGetValue(HeightTipQueueBlocks, out block));

        return true;
      }

      public double GetDifficultyAccumulatedHeaderTip()
      {
        return DifficultyAccumulatedHeightTip;
      }
    }
  }
}
