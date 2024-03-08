using BTokenLib;
using System.Windows.Controls;

namespace BTokenWPF
{
  public partial class ListBoxItemTX : UserControl
  {
    public TX TX;

    public ListBoxItemTX(TX tX)
    {
      TX = tX;

      InitializeComponent();

      LabelTXHash.Content = tX.Hash.ToHexString();

      if(tX is TXBitcoin)
      {
        TXBitcoin tXBitcoin = (TXBitcoin)tX;
        LabelCountInputs.Content = $"Number of Inputs: {tXBitcoin.Inputs.Count}";
        LabelCountOutputs.Content = $"Number of Outputs: {tXBitcoin.TXOutputs.Count}";
      }
      LabelFee.Content = $"Fee: {tX.Fee}";
    }
  }
}
