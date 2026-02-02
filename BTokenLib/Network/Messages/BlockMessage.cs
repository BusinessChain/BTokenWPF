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
        public Header HeaderDownload;
        public Block BlockDownload;

        public BlockMessage()
          : base("block") { }

        public BlockMessage(byte[] bufferBlock, int lengthPayload)
          : base(
              "block",
              bufferBlock,
              0,
              lengthPayload)
        { }

        public override void ParsePayload()
        {
          if (HeaderDownload == null)
            throw new ProtocolException($"Received unrequested block message.");

          BlockDownload.LengthBufferPayload = LengthDataPayload;

          BlockDownload.Parse(HeaderDownload.Height);

          if (!BlockDownload.Header.Hash.IsAllBytesEqual(HeaderDownload.Hash))
            throw new ProtocolException(
              $"Received unexpected block {BlockDownload} at height {BlockDownload.Header.Height} from peer {this}.\n" +
              $"Requested was {HeaderDownload}.");
        }

        public override async Task Run(Peer peer)
        {
          peer.InsertBlock(BlockDownload);

          BlockDownload = null;
          HeaderDownload = null;

          peer.StartBlockDownload();
        }
      }
    }
  }
}