using System;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class MessageBlock : MessageNetworkProtocol
      {

        public MessageBlock()
          : base("block") { }

        public MessageBlock(byte[] bufferBlock, int lengthPayload)
          : base(
              "block",
              bufferBlock,
              0,
              lengthPayload)
        { }

        public override MessageNetworkProtocol Create()
        {
          return new MessageBlock();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}