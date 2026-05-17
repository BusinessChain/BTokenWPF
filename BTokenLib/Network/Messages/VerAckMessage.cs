using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    class VerAckMessage : MessageNetworkProtocol
    {
      public const string Command = "verack";


      public VerAckMessage()
      { }


      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}