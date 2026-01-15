using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    class SendHeadersMessage : MessageNetwork
    {
      public SendHeadersMessage() 
        : base("sendheaders") { }

      public override MessageNetwork Create()
      {
        return new SendHeadersMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}
