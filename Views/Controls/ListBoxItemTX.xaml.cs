using BTokenLib;
using System.Windows.Controls;

namespace BTokenWPF
{
  public partial class ListBoxItemTX : UserControl
  {
    TX TX;

    public ListBoxItemTX(TX tX)
    {
      TX = tX;

      InitializeComponent();

      TextBoxTXHash.Text = tX.Hash.ToHexString();
      TextBoxTXRaw.Text = tX.TXRaw.ToArray().ToHexString();

      TextBoxCountInputs.AppendText(tX.TXInputs.Count.ToString());
      TextBoxCountOutputs.AppendText(tX.TXOutputs.Count.ToString());
      TextBoxFee.AppendText(tX.Fee.ToString());
    }
  }
}
