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
        public Header HeaderDownload;
        public Block BlockDownload;

        public BlockMessage()
          : base("block") { }


        public override byte[] GetPayloadBuffer()
        {
          return BlockDownload.Buffer;
        }

        public override void Run(Peer peer)
        {
          if (HeaderDownload == null)
            throw new ProtocolException($"Received unrequested block message.");

          BlockDownload.LengthDataPayload = LengthDataPayload;

          BlockDownload.Parse(HeaderDownload.Height);

          if (!BlockDownload.Header.Hash.IsAllBytesEqual(HeaderDownload.Hash))
            throw new ProtocolException(
              $"Received unexpected block {BlockDownload} at height {BlockDownload.Header.Height} from peer {this}.\n" +
              $"Requested was {HeaderDownload}.");

          peer.Synchronization.InsertBlock(BlockDownload);

          Network.UpdateSynchronization(peer.Synchronization);

          RequestBlock(peer);
        }

        public void RequestBlock(Peer peer)
        {
          if (peer.Synchronization?.TryFetchBlockDownload(
            out Header headerDownload,
            out Block blockDownload) == true)
          {
            HeaderDownload = headerDownload;
            BlockDownload = blockDownload;

            peer.SendMessage(new GetDataMessage(
              InventoryType.MSG_BLOCK, headerDownload.Hash));
          }
        }
      }
    }
  }
}