using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class InvMessage : MessageNetworkProtocol
      {
        public List<Inventory> Inventories = new();

        public InvMessage()
          : base("inv") { }

        public InvMessage(List<Inventory> inventories)
          : base("inv")
        {
          Inventories = inventories;

          List<byte> payload = new();

          payload.AddRange(VarInt.GetBytes(inventories.Count));

          Inventories.ForEach(
            i => payload.AddRange(i.GetBytes()));

          Payload = payload.ToArray();
          LengthDataPayload = Payload.Length;
        }

        public InvMessage(byte[] buffer)
          : base(
              "inv",
              buffer)
        {
          int startIndex = 0;

          int inventoryCount = VarInt.GetInt(
            Payload,
            ref startIndex);

          for (int i = 0; i < inventoryCount; i++)
            Inventories.Add(Inventory.Parse(
              Payload,
              ref startIndex));
        }


        public override MessageNetworkProtocol Create()
        {
          return new InvMessage();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}
