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
      class GetDataMessage : MessageNetworkProtocol
      {
        public List<Inventory> Inventories = new();


        public GetDataMessage()
          : base("getdata")
        {

        }

        public GetDataMessage(byte[] buffer)
          : base("getdata", buffer)
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

        public GetDataMessage(InventoryType inventoryType, byte[] hash)
          : this(new List<Inventory>() { new Inventory(inventoryType, hash) })
        { }

        public GetDataMessage(List<Inventory> inventories)
          : base("getdata")
        {
          Inventories = inventories;

          List<byte> payload = new();

          payload.AddRange(VarInt.GetBytes(Inventories.Count()));

          for (int i = 0; i < Inventories.Count(); i++)
            payload.AddRange(Inventories[i].GetBytes());

          Payload = payload.ToArray();
          LengthDataPayload = Payload.Length;
        }

        public override MessageNetworkProtocol Create()
        {
          return new GetDataMessage();
        }


        public override void Run(Peer peer)
        {

        }
      }
    }
  }
}
