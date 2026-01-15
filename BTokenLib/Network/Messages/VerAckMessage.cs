using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    class VerAckMessage : MessageNetwork
    {
      public VerAckMessage() 
        : base("verack")
      { }


      public override MessageNetwork Create()
      {
        return new VerAckMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}
