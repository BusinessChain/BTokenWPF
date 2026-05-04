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


        public GetDataMessage(Network network)
          : base("getdata", network)
        { }

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

        public override void Run(Peer peer)
        {
          int startIndex = 0;

          int inventoryCount = VarInt.GetInt(Payload, ref startIndex);

          for (int i = 0; i < inventoryCount; i++)
          {
            Inventory inventory = Inventory.Parse(Payload, ref startIndex);

            if (inventory.Type == InventoryType.MSG_TX)
            {
              if (Token.TryGetTX(inventory.Hash, out TX tXInPool))
                await SendMessage(new TXMessage(tXInPool.TXRaw));
              else
                await SendMessage(new NotFoundMessage(
                  new List<Inventory>() { inventory }));
            }
            else if (inventory.Type == InventoryType.MSG_BLOCK)
            {
              if (Network.TryLoadBlock(inventory.Hash, out byte[] buffer))
                peer.SendBlock(buffer);
            }
            else if (inventory.Type == InventoryType.MSG_DB)
            {
            }
          }
        }
      }
    }
  }
}
