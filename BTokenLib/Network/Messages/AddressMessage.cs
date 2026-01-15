using System;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    class AddressMessage : MessageNetwork
    {
      public List<NetworkAddress> NetworkAddresses = new();

      public AddressMessage()
        : base("addr") { }

      public AddressMessage(byte[] messagePayload)
        : base("addr", messagePayload)
      {
        int startIndex = 0;

        int addressesCount = VarInt.GetInt(
          Payload,
          ref startIndex);

        for (int i = 0; i < addressesCount; i++)
        {
          NetworkAddress address = NetworkAddress.ParseAddress(
              Payload, ref startIndex);

          if (NetworkAddresses.Any(
            a => a.IPAddress.ToString() == address.IPAddress.ToString()))
            throw new ProtocolException("Duplicate network address advertized.");

          NetworkAddresses.Add(address);
        }
      }


      public override MessageNetwork Create()
      {
        return new AddressMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}
