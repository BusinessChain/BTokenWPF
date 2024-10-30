using BTokenLib;
using System.Windows.Controls;

namespace BTokenWPF.Views
{
  public partial class ListBoxItemPeer : UserControl
  {
    public ListBoxItemPeer(Network.Peer peer)
    {
      InitializeComponent();

      LabelPeer.Content = $"Peer: {peer.IPAddress} | {peer.Connection}";
      LabelState.Content = $"State: {peer.State}";
      LabelTimePeerCreation.Content = $"Time created: {peer.TimePeerCreation}";
      LabelTimeLastSynchronization.Content = $"Last synchronization: {peer.TimeLastSync}";
    }
  }
}
