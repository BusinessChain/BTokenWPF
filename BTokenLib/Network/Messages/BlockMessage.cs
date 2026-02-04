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
              lengthPayload)
        { }

        public override byte[] GetPayloadBuffer()
        {
          return BlockDownload.Buffer;
        }

        public override void Run(Peer peer)
        {
          ParsePayload();

          peer.InsertBlock(BlockDownload);
          peer.StartBlockDownload();
        }

        void ParsePayload()
        {
          if (HeaderDownload == null)
            throw new ProtocolException($"Received unrequested block message.");

          BlockDownload.LengthDataPayload = LengthDataPayload;

          BlockDownload.Parse(HeaderDownload.Height);

          if (!BlockDownload.Header.Hash.IsAllBytesEqual(HeaderDownload.Hash))
            throw new ProtocolException(
              $"Received unexpected block {BlockDownload} at height {BlockDownload.Header.Height} from peer {this}.\n" +
              $"Requested was {HeaderDownload}.");
        }
      }
    }
  }
}