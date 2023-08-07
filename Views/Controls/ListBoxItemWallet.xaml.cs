using System.Windows;
using System.Windows.Controls;

using BTokenLib;

namespace BTokenWPF
{
  public partial class ListBoxItemWallet : UserControl
  {
    public ListBoxItemWallet()
    {
      InitializeComponent();

      LabelTXID.HorizontalAlignment = HorizontalAlignment.Center;
      LabelTXID.Content = "TXID";
      LabelIndex.HorizontalAlignment = HorizontalAlignment.Center;
      LabelIndex.Content = $"Index";
      LabelValue.HorizontalAlignment = HorizontalAlignment.Center;
      LabelValue.Content = $"Value";
    }

    public ListBoxItemWallet(TXOutputWallet tXOutputWallet)
    {
      InitializeComponent();

      LabelTXID.Content = $"{tXOutputWallet.TXID.ToHexString().Substring(0, 16) + " ..."}";
      LabelIndex.Content = $"{tXOutputWallet.Index}";
      LabelValue.Content = $"{tXOutputWallet.Value}";
    }
  }
}
