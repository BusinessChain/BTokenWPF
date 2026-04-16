using System;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class BlockMessage : MessageNetworkProtocol
      {
        Network Network;
        public Block BlockDownload;

        public BlockMessage()
          : base("block") { }


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

          peer.SendBlockRequest(BlockDownload);
        }
      }
    }
  }
}