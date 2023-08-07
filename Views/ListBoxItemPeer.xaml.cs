using BTokenLib;
using System.Windows.Controls;

namespace BTokenWPF.Views
{
  public partial class ListBoxItemPeer : UserControl
  {
    Network.Peer Peer;

    public ListBoxItemPeer(Network.Peer peer)
    {
      Peer = peer;

      InitializeComponent();

      TextBoxPeer.Text = $"{peer}";
    }
  }
}
