using System;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    class BlockMessage : MessageNetworkProtocol
    {
      public const string Command = "block";

      public Block BlockDownload;


      public BlockMessage(Block blockDownload)
        : base()
      {
        BlockDownload = blockDownload;
      }

      public override byte[] GetPayloadBuffer()
      {
        return BlockDownload.Buffer;
      }

      public override async Task Run(Peer peer)
      {
        if (BlockDownload?.Header == null)
          throw new ProtocolException($"Received unrequested block message.");

        DOSMonitor.Decrement(1);

        BlockDownload.LengthDataPayload = LengthDataPayload;

        BlockDownload.Parse();

        await peer.Network.InsertBlock(peer, BlockDownload);

        if (BlockDownload.Header != null)
          GetDataMessage.SendBlockRequest(peer, BlockDownload.Header.Hash);
      }

      public static async Task SendBlock(Peer peer, Block block)
      {
        await peer.SendMessage(Command, block.LengthDataPayload, block.Buffer);
      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}