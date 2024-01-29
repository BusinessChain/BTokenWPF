using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BTokenLib
{
  public class TXBitcoin : TX
  {
    public List<TXInput> TXInputs = new();
    public List<TXOutput> TXOutputs = new();
  }
}
