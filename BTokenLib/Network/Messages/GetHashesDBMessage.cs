using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    class GetHashesDBMessage : MessageNetwork
    {
      public GetHashesDBMessage()
        : base("getHashesDB")
      { }


      public override MessageNetwork Create()
      {
        return new GetHashesDBMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}