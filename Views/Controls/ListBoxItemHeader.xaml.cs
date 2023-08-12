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

      LabelHeight.Content = $"Height: {header.Height}";
      LabelHash.Content = header.Hash.ToHexString().Substring(0,24) + " ...";

      if (header.HashChild != null)
        LabelHashChild.Content = $"{char.ConvertFromUtf32(0x21b3)} " +
          $"{header.HashChild.ToHexString().Substring(0, 24)} ...";
      else
        Grid.Children.Remove(LabelHashChild);
    }
  }
}
