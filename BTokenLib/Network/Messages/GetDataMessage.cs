using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class GetDataMessage : MessageNetworkProtocol
      {
        const string Command = "getdata";

        public List<Inventory> Inventories = new();


        public GetDataMessage(Network network)
          : base(network)
        {
          DOSMonitor = new DOSMonitorPer10Minutes(maxLevel: 5);
        }

        public GetDataMessage(List<Inventory> inventories)
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
          DOSMonitor.Increment(1);

          int startIndex = 0;

          int inventoryCount = VarInt.GetInt(Payload, ref startIndex);

          for (int i = 0; i < inventoryCount; i++)
          {
            Inventory inventory = Inventory.Parse(Payload, ref startIndex);

            if (inventory.Type == InventoryType.MSG_TX)
            {
              if (Token.TryGetTX(inventory.Hash, out TX tXInPool))
                await SendMessage(new TXMessage(tXInPool.TXRaw));
            }
            else if (inventory.Type == InventoryType.MSG_BLOCK)
            {
              if (Network.TryLoadBlock(inventory.Hash, out byte[] buffer))
                BlockMessage.SendBlock(peer, buffer);
            }
            else if (inventory.Type == InventoryType.MSG_DB)
            {
            }
          }
        }

        public static async Task SendBlockRequest(Peer peer, byte[] hash)
        {
          List<byte> payload = new();

          payload.AddRange(VarInt.GetBytes(1));
          payload.AddRange(BitConverter.GetBytes((uint)InventoryType.MSG_BLOCK));
          payload.AddRange(hash);

          byte[] buffer = payload.ToArray();

          await peer.SendMessage(Command, buffer.Length, buffer);
        }

        public override string GetCommand()
        {
          return Command;
        }
      }
    }
  }
}
