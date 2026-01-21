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
      class SendHeadersMessage : MessageNetworkProtocol
      {
        public SendHeadersMessage()
          : base("sendheaders") { }

        public override MessageNetworkProtocol Create()
        {
          return new SendHeadersMessage();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}
