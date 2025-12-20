using System;
using System.IO;
using System.Collections.Generic;

namespace BTokenLib
{
  public abstract class TXPool
  {
    public abstract void RemoveTXs(IEnumerable<byte[]> hashesTX);

    public abstract void AddTX(TX tX);

    public abstract bool TryGetTX(byte[] hashTX, out TX tX);

    public abstract List<TX> GetTXs(int countMax, out long feeTXs);

    public abstract void Clear();
  }
}
