using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;


namespace BTokenLib
{
  public class PoolTXBitcoin : TXPool
  {
    TokenBitcoin Token;

    readonly object LOCK_TXsPool = new(); 
    const bool FLAG_ENABLE_RBF = true;

    int SequenceNumberTX;

    Dictionary<byte[], (TXBitcoin tX, int sequenceNumberTX)> TXPoolDict =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<(TXInputBitcoin, TXBitcoin)>> InputsPool =
      new(new EqualityComparerByteArray());

    List<TX> TXsGet = new();
    int CountMaxTXsGet;

    Dictionary<int, bool> FlagTXAddedPerThreadID = new();


    public PoolTXBitcoin(TokenBitcoin token) 
    {
      Token = token;
    }

    public override bool TryGetTX(byte[] hashTX, out TX tX)
    {
      lock (LOCK_TXsPool)
      {
        if(TXPoolDict.TryGetValue(hashTX, out (TXBitcoin tX, int) itemTXPool))
        {
          tX = itemTXPool.tX;
          return true;
        }

        tX = null;
        return false;
      }
    }

    public bool GetFlagTXAddedSinceLastInquiry()
    {
      int iDThread = Thread.CurrentThread.ManagedThreadId;

      lock (LOCK_TXsPool)
      {
        if(FlagTXAddedPerThreadID.TryGetValue(iDThread, out bool flagTXAdded) && !flagTXAdded)
          return false;

        FlagTXAddedPerThreadID[iDThread] = false;
        return true;
      }
    }

    public override bool TryAddTX(TX tX)
    {
      TXBitcoin tXBitcoin = (TXBitcoin)tX;

      bool flagRemoveTXInPoolBeingRBFed = false;
      TX tXInPoolBeingRBFed = null;

      try
      {
        lock (LOCK_TXsPool)
        {
          foreach (TXInputBitcoin tXInput in tXBitcoin.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInputBitcoin, TXBitcoin)> inputsInPool))
              foreach ((TXInputBitcoin input, TX tX) tupleInputsInPool in inputsInPool)
                if (tupleInputsInPool.input.OutputIndex == tXInput.OutputIndex)
                {
                  ($"Output {tXInput.TXIDOutput.ToHexString()} - {tXInput.OutputIndex} referenced by tX {tX} " +
                    $"already referenced by tX {tupleInputsInPool.tX}.").Log(this, Token.LogEntryNotifier);

                  if (
                    FLAG_ENABLE_RBF &&
                    tXInput.Sequence > tupleInputsInPool.input.Sequence)
                  {
                    ($"Replace tX {tupleInputsInPool.tX} (sequence = {tupleInputsInPool.input.Sequence}) " +
                      $"with tX {tX} (sequence = {tXInput.Sequence}).").Log(this, Token.LogEntryNotifier);

                    flagRemoveTXInPoolBeingRBFed = true;
                    tXInPoolBeingRBFed = tupleInputsInPool.tX;
                  }
                  else
                    return false;
                }

          if (flagRemoveTXInPoolBeingRBFed)
            RemoveTX(tXInPoolBeingRBFed.Hash, flagRemoveRecursive: true);

          TXPoolDict.Add(tXBitcoin.Hash, (tXBitcoin, SequenceNumberTX++));

          foreach (TXInputBitcoin tXInput in tXBitcoin.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInputBitcoin input, TXBitcoin)> inputsInPool))
              inputsInPool.Add((tXInput, tXBitcoin));
            else
              InputsPool.Add(tXInput.TXIDOutput, new List<(TXInputBitcoin, TXBitcoin)>() { (tXInput, tXBitcoin) });

          foreach (int key in FlagTXAddedPerThreadID.Keys.ToList())
            FlagTXAddedPerThreadID[key] = true;

          return true;
        }
      }
      catch (Exception ex)
      {
        $"Exception {ex.GetType().Name} when trying to insert tx {tX} in TXPool."
          .Log(this, Token.LogEntryNotifier);

        return false;
      }
    }

    public override void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
      foreach (byte[] hashTX in hashesTX)
        RemoveTX(hashTX, flagRemoveRecursive: false);

      SequenceNumberTX = 0;

      var orderedItems = TXPoolDict.OrderBy(i => i.Value.sequenceNumberTX).ToList();

      for (int i = 0; i < orderedItems.Count; i++)
      {
        TXPoolDict[orderedItems[i].Key] = (orderedItems[i].Value.tX, SequenceNumberTX++);
      }
    }

    void RemoveTX(byte[] hashTX, bool flagRemoveRecursive)
    {
      lock (LOCK_TXsPool)
        if (TXPoolDict.Remove(hashTX, out (TXBitcoin tX, int) itemInPool))
        {
          List<(TXInputBitcoin input, TXBitcoin)> tupelInputs = null;

          foreach (TXInputBitcoin tXInput in itemInPool.tX.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out tupelInputs))
            {
              tupelInputs.RemoveAll(t => t.input.OutputIndex == tXInput.OutputIndex);

              if (tupelInputs.Count == 0)
                InputsPool.Remove(tXInput.TXIDOutput);
            }

          if (flagRemoveRecursive && InputsPool.TryGetValue(hashTX, out tupelInputs))
            foreach ((TXInputBitcoin input, TXBitcoin tX) tupelInputInPool in tupelInputs.ToList())
              RemoveTX(tupelInputInPool.tX.Hash, flagRemoveRecursive: true);
        }
    }

    public override List<TX> GetTXs(int countMax, out long feeTXs)
    {
      feeTXs = 0;

      lock (LOCK_TXsPool)
      {
        FlagTXAddedPerThreadID[Thread.CurrentThread.ManagedThreadId] = false;

        TXsGet.Clear();
        CountMaxTXsGet = countMax;

        foreach (KeyValuePair<byte[], (TXBitcoin tX, int)> itemInPool in TXPoolDict)
          if (TXsGet.Count < CountMaxTXsGet)
            ExtractBranch(itemInPool.Value.tX);
          else
            break;

        return TXsGet.ToList();
      }
    }

    void ExtractBranch(TXBitcoin tXRoot)
    {
      if (TXsGet.Contains(tXRoot))
        return;

      List<TXBitcoin> tXsBranch = new() { tXRoot };

      TraceTXToLeaf(tXsBranch);

      foreach (TXBitcoin tXBranch in tXsBranch)
      {
        foreach (TXInputBitcoin input in tXBranch.Inputs)
        {
          if (TXsGet.Count >= CountMaxTXsGet)
            return;

          if (TXPoolDict.TryGetValue(input.TXIDOutput, out (TXBitcoin tX, int) itemRootSubBranch))
            ExtractBranch(itemRootSubBranch.tX);
        }

        TXsGet.Add(tXBranch);
      }
    }

    void TraceTXToLeaf(List<TXBitcoin> tXsBranch)
    {
      foreach (TXInputBitcoin input in tXsBranch[0].Inputs)
        if (TXPoolDict.TryGetValue(input.TXIDOutput, out (TXBitcoin tX, int) itemInPool) &&
            !TXsGet.Contains(itemInPool.tX))
        {
          tXsBranch.Insert(0, itemInPool.tX);
          TraceTXToLeaf(tXsBranch);
          return;
        }
    }

    public override void Clear()
    {
      TXPoolDict.Clear();
      InputsPool.Clear();
    }
  }
}
