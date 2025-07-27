using System.Windows.Controls;
using BTokenLib;


namespace BTokenWPF
{
  public partial class ListBoxItemDBEntry : UserControl
  {
    public ListBoxItemDBEntry(Account account, string locationAccount, int indexSource)
    {
      InitializeComponent();

      LabelHashAccount.Content = $"AccountID: {account}";
      LabeValue.Content = "Value: " + account.Balance;
      LabelBlockHeighInit_Nonce.Content = $"Nonce: {account.BlockHeightAccountInit} - {account.Nonce}";
      LabelLocation_Index.Content = $"Location: {locationAccount}{indexSource}";
    }
  }
}
