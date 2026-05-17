using System;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    class BlockMessage : MessageNetworkProtocol
    {
      public const string Command = "block";

      Network Network;
      public Block BlockDownload;


      public BlockMessage(Network network, Block blockDownload)
        : base()
      {
        Network = network;
        BlockDownload = blockDownload;
      }

      public override byte[] GetPayloadBuffer()
      {
        return BlockDownload.Buffer;
      }

      public override void Run(Peer peer)
      {
        if (BlockDownload?.Header == null)
          throw new ProtocolException($"Received unrequested block message.");

        BlockDownload.LengthDataPayload = LengthDataPayload;

        BlockDownload.Parse();

        Network.InsertBlock(BlockDownload);

        if (BlockDownload.Header != null)
          GetDataMessage.SendBlockRequest(peer, BlockDownload.Header.Hash);
      }

      public static async Task SendBlock(Peer peer, byte[] buffer)
      {
        await peer.SendMessage(Command, buffer.Length, buffer);
      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}