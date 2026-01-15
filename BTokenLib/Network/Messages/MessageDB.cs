using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class Network
  {
    class MessageDB : MessageNetwork
    {
      public MessageDB()
        : base("dataDB")
      { }

      public MessageDB(byte[] dataDB)
        : base("dataDB",dataDB)
      { }

      public override MessageNetwork Create()
      {
        return new MessageDB();
      }
    }
  }
}