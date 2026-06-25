using BTokenLib;
using System.Windows.Controls;

namespace BTokenWPF
{
  public partial class ListBoxItemTX : UserControl
  {
    public ListBoxItemTX(Token.TX tX)
    {
      //TX = tX;

      InitializeComponent();

      LabelTXHash.Content = tX.Hash.ToHexString();

      if(tX is TokenBitcoin.TXBitcoin tXBitcoin)
      {
        LabelCountInputs.Content = $"Number of Inputs: {tXBitcoin.Inputs.Count}";
        LabelCountOutputs.Content = $"Number of Outputs: {tXBitcoin.TXOutputs.Count}";
      }
      if(tX is TokenBToken.TXBToken tXBToken)
        LabelFee.Content = $"Fee: {tXBToken.Fee}";
    }
  }
}
