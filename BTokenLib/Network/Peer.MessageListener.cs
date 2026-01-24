using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      MessageNetworkProtocol MessageNetwork;

      Dictionary<StateProtocol, StateProtocolPeer> StatesPeerProtocol = new()
      {
        { StateProtocol.Handshake, new StateHandshake() },
        { StateProtocol.Idle, new StateIdle() },
        { StateProtocol.HeaderDownload, new StateHeaderDownload() },
      };

      public StateProtocol StateCurrent = StateProtocol.Handshake;


      public async Task StartStateMachine()
      {
        try
        {
          while (true)
            StateCurrent = await StatesPeerProtocol[StateCurrent].Run(this);
        }
        catch (Exception ex)
        {
          Log($"{ex.GetType().Name} in state {StateCurrent.ToString()}: \n{ex.Message}");
          Dispose();
        }
      }

      async Task<MessageNetworkProtocol> ReceiveNextMessage()
      {
        await MessageNetwork.Receive(this);
        return MessageNetwork;
      }

      async Task ReadBytes(byte[] buffer, int bytesToRead)
      {
        int offset = 0;

        try
        {
          while (bytesToRead > 0)
          {
            int chunkSize = await NetworkStream.ReadAsync(
              buffer,
              offset,
              bytesToRead,
              Cancellation.Token)
              .ConfigureAwait(false);

            if (chunkSize == 0)
              throw new IOException("Stream returns 0 bytes signifying end of stream.");

            offset += chunkSize;
            bytesToRead -= chunkSize;
          }
        }
        catch (OperationCanceledException)
        {
          Log($"Timeout occured when waiting for next message.");
          throw;
        }
      }

      void SetTimer(string descriptionTimeOut = "", int millisecondsTimer = int.MaxValue)
      {
        if (descriptionTimeOut != "")
          Log($"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.");

        Cancellation.CancelAfter(millisecondsTimer);
      }
    }
  }
}
