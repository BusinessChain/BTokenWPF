using System.Windows.Controls;
using BTokenLib;

namespace BTokenWPF
{
  public partial class ListBoxItemHeader : UserControl
  {
    public Header Header;

    public ListBoxItemHeader(Header header)
    {
      InitializeComponent();

      Header = header;

      TextBlockHeight.Text = $"Height: {header.Height}";
      TextBlockHash.Text = header.Hash.ToHexString();

      if (header.HashChild != null)
        TextBlockHashChild.Text = $"{char.ConvertFromUtf32(0x21b3)} {header.HashChild.ToHexString()}";
      else
        Grid.Children.Remove(TextBlockHashChild);
    }
  }
}
