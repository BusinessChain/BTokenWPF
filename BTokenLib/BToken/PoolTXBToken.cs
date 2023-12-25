using BTokenLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTokenWPF
{
  public class PoolTXBToken
  {
    readonly object LOCK_TXsPool = new();

    Dictionary<byte[], TXBToken> TXPoolDict =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<(TXInput, TXBitcoin)>> InputsPool =
      new(new EqualityComparerByteArray());


    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
    }

    public bool TryAddTX(TXBToken tX)
    {
      // falls ungültig dann exception werfen. 
      // Return false falls noch überprüfung DB erforderlich. 
      // bei einer Verkettung wurde die Parent TX ja schon von der DB überprüft.

      bool flagRemoveTXInPoolBeingRBFed = false;
      TX tXInPoolBeingRBFed = null;

      try
      {
        lock (LOCK_TXsPool)
        {

          if(InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInput, TXBitcoin)> inputsInPool))




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

          foreach (TXInput tXInput in tX.TXInputs)
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

    public List<TX> GetTXs(out int countTXsPool, int countMax = int.MaxValue)
    {
      countTXsPool = 0;
      return null;
    }

    public bool TryGetTX(byte[] hashTX, out TXBToken tX)
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.TryGetValue(hashTX, out tX);
    }

    public int GetCountTXs()
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.Count;
    }
  }
}
