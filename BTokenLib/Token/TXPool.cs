using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  public abstract class TXPool
  {
    public abstract void RemoveTXs(IEnumerable<byte[]> hashesTX);

    public abstract bool TryAddTX(TX tX);

    public abstract void Load();

    public abstract bool TryGetTX(byte[] hashTX, out TX tX);

    public abstract List<TX> GetTXs(int countMax = int.MaxValue);
  }
}
