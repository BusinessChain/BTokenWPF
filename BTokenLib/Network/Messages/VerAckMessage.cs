using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class VerAckMessage : MessageNetworkProtocol
      {
        public VerAckMessage()
          : base("verack")
        { }


        public override MessageNetworkProtocol Create()
        {
          return new VerAckMessage();
        }
      }
    }
  }
}