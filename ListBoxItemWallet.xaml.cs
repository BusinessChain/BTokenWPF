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

      TextBlockTXID.HorizontalAlignment = HorizontalAlignment.Center;
      TextBlockTXID.Text = "TXID";
      TextBlockIndex.HorizontalAlignment = HorizontalAlignment.Center;
      TextBlockIndex.Text = $"Index";
      TextBlockValue.HorizontalAlignment = HorizontalAlignment.Center;
      TextBlockValue.Text = $"Value";
    }

    public ListBoxItemWallet(TXOutputWallet tXOutputWallet)
    {
      InitializeComponent();

      TextBlockTXID.Text = $"{tXOutputWallet.TXID.ToHexString().Substring(0, 16) + " ..."}";
      TextBlockIndex.Text = $"{tXOutputWallet.Index}";
      TextBlockValue.Text = $"{tXOutputWallet.Value}";
    }
  }
}
