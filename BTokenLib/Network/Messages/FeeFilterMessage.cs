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
      class FeeFilterMessage : MessageNetwork
      {
        public ulong FeeFilterValue { get; private set; }

        public FeeFilterMessage()
          : base("feefilter")
        { }

        public FeeFilterMessage(byte[] messagePayload)
          : base("feefilter", messagePayload)
        { }


        public override MessageNetwork Create()
        {
          return new FeeFilterMessage();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}
