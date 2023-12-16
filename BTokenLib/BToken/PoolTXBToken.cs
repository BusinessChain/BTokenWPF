using BTokenLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenWPF
{
  public class PoolTXBToken
  {
    readonly object LOCK_TXsPool = new();

    Dictionary<byte[], TXBToken> TXPoolDict =
      new(new EqualityComparerByteArray());


    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
    }

    public bool TryAddTX(TX tX)
    {

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
