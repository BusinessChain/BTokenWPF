using BTokenLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace BTokenWPF
{
  public class PoolTXBitcoin
  {
    readonly object LOCK_TXsPool = new(); 
    const bool FLAG_ENABLE_RBF = true;

    Dictionary<byte[], List<(TXInput, TXBitcoin)>> InputsPool =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], TXBitcoin> TXPoolDict =
      new(new EqualityComparerByteArray());

    List<TX> TXsGet = new();
    int CountMaxTXsGet;



    public bool TryGetTX(byte[] hashTX, out TXBitcoin tX)
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.TryGetValue(hashTX, out tX);
    }

    public int GetCountTXs()
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.Count;
    }

    public bool TryAddTX(TXBitcoin tX)
    {
      bool flagRemoveTXInPoolBeingRBFed = false;
      TX tXInPoolBeingRBFed = null;

      try
      {
        lock (LOCK_TXsPool)
        {
          foreach (TXInput tXInput in tX.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInput, TXBitcoin)> inputsInPool))
              foreach ((TXInput input, TX tX) tupelInputsInPool in inputsInPool)
                if (tupelInputsInPool.input.OutputIndex == tXInput.OutputIndex)
                {
                  Debug.WriteLine(
                    $"Output {tXInput.TXIDOutput.ToHexString()} / {tXInput.OutputIndex} referenced by tX {tX} " +
                    $"already referenced by tX {tupelInputsInPool.tX}.");

                  if (
                    FLAG_ENABLE_RBF &&
                    tXInput.Sequence > tupelInputsInPool.input.Sequence)
                  {
                    Debug.WriteLine(
                      $"Replace tX {tupelInputsInPool.tX} (sequence = {tupelInputsInPool.input.Sequence}) " +
                      $"with tX {tX} (sequence = {tXInput.Sequence}).");

                    flagRemoveTXInPoolBeingRBFed = true;
                    tXInPoolBeingRBFed = tupelInputsInPool.tX;
                  }
                  else
                    return false;
                }

          if (flagRemoveTXInPoolBeingRBFed)
            RemoveTXRecursive(tXInPoolBeingRBFed.Hash);

          TXPoolDict.Add(tX.Hash, tX);

          foreach (TXInput tXInput in tX.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInput input, TXBitcoin)> inputsInPool))
              inputsInPool.Add((tXInput, tX));
            else
              InputsPool.Add(tXInput.TXIDOutput, new List<(TXInput, TXBitcoin)>() { (tXInput, tX) });

          return true;
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Exception {ex.GetType().Name} when trying to insert tx {tX} in TXPool.");
        return false;
      }
    }

    public List<TX> GetTXs(out int countTXsPool, int countMax)
    {
      lock (LOCK_TXsPool)
      {
        TXsGet.Clear();
        CountMaxTXsGet = countMax;

        countTXsPool = TXPoolDict.Count;

        foreach (KeyValuePair<byte[], TXBitcoin> tXInPool in TXPoolDict)
          if (TXsGet.Count < CountMaxTXsGet)
            ExtractBranch(tXInPool.Value);
          else
            break;

        return TXsGet.ToList();
      }
    }

    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
      lock (LOCK_TXsPool)
        foreach (byte[] hashTX in hashesTX)
          RemoveTXRecursive(hashTX);
    }

    /// <summary>
    /// Removes a tX and all tXs that reference its outputs.
    /// </summary>
    void RemoveTXRecursive(byte[] hashTX)
    {
      if (TXPoolDict.Remove(hashTX, out TXBitcoin tX))
      {
        List<(TXInput input, TXBitcoin)> tupelInputs = null;

        foreach (TXInput tXInput in tX.Inputs)
          if (InputsPool.TryGetValue(tXInput.TXIDOutput, out tupelInputs))
          {
            tupelInputs.RemoveAll(t => t.input.OutputIndex == tXInput.OutputIndex);

            if (tupelInputs.Count == 0)
              InputsPool.Remove(tXInput.TXIDOutput);
          }

        if (InputsPool.TryGetValue(hashTX, out tupelInputs))
          foreach ((TXInput input, TXBitcoin tX) tupelInputInPool in tupelInputs.ToList())
            RemoveTXRecursive(tupelInputInPool.tX.Hash);
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
        foreach (TXInput input in tXBranch.Inputs)
        {
          if (TXsGet.Count >= CountMaxTXsGet)
            return;

          if (TXPoolDict.TryGetValue(input.TXIDOutput, out TXBitcoin tXRootSubBranch))
            ExtractBranch(tXRootSubBranch);
        }

        TXsGet.Add(tXBranch);
      }
    }

    void TraceTXToLeaf(List<TXBitcoin> tXsBranch)
    {
      foreach (TXInput input in tXsBranch[0].Inputs)
        if (TXPoolDict.TryGetValue(input.TXIDOutput, out TXBitcoin tXInPool) &&
            !TXsGet.Contains(tXInPool))
        {
          tXsBranch.Insert(0, tXInPool);
          TraceTXToLeaf(tXsBranch);
          return;
        }
    }
  }
}
