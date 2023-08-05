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

      LabelHash.Content = header.Hash.ToHexString();
      LabelHashChild.Content = header.HashChild.ToHexString();
    }
  }
}
