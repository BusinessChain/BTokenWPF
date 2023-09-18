using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using BTokenLib;
using BTokenWPF.Views;

namespace BTokenWPF
{
  public partial class WindowToken : Window
  {
    public Token Token;

    public WindowToken(Token token)
    {
      InitializeComponent();

      Token = token;
      LabelToken.Content = token.GetName().Substring("Token".Count());

      UpdateControlsWindowToken();
    }

    async Task UpdateControlsWindowToken()
    {
      try
      {
        CheckBoxEnableOutboundConnections.IsChecked = Token.Network.FlagEnableOutboundConnections;

        while (true)
        {
          if (Token.TryLock())
          {
            UpdateListBoxHeaderchain();

            UpdateTextBoxWallet();

            //UpdateListBoxTXPool();

            Token.ReleaseLock();
          }

          UpdateControlsNetwork();

          await Task.Delay(1000);
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          $"{ex.Message}: {ex.StackTrace}",
          $"{ex.GetType().Name}",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
    }

    void UpdateControlsNetwork()
    {
      List<Network.Peer> peers = Token.Network.GetPeers();

      LabelCountPeers.Content = $"Number of peers: {peers.Count}";
      LabelStateNetworkConnector.Content = $"State network connector: {Token.Network.State}";
      LabelIsStateSynchronizing.Content = $"Network synchronizing: {Token.Network.IsStateSynchronizing}";
      Token.Network.FlagEnableOutboundConnections = (bool)CheckBoxEnableOutboundConnections.IsChecked;

      ListBoxPeers.Items.Clear();

      foreach (Network.Peer peer in peers)
        ListBoxPeers.Items.Add(new ListBoxItemPeer(peer));
    }

    Header HeaderTipAtLastUpdate;
    void UpdateTextBoxWallet()
    {
      LabelBalanceSatoshies.Content = $"Balance: {Token.Wallet.Balance}";
      LabelBalanceSatoshiesUnconfirmed.Content = $"({Token.Wallet.BalanceUnconfirmed})";

      HeaderTipAtLastUpdate = Token.HeaderTip;

      ListBoxWallet.Items.Clear();
      ListBoxWallet.Items.Add(new ListBoxItemWallet());

      foreach (TXOutputWallet tXOutputWallet in Token.Wallet.Outputs)
        ListBoxWallet.Items.Add(new ListBoxItemWallet(tXOutputWallet));
    }

    //int CountTXsPoolOld;
    //void UpdateListBoxTXPool()
    //{
    //  int countTXsPool = Token.TXPool.GetCountTXs();

    //  if (countTXsPool == CountTXsPoolOld)
    //    return;

    //  List<TX> tXs = Token.TXPool.GetTXs(out CountTXsPoolOld);

    //  ListBoxTXPool.Items.Clear();

    //  foreach (TX tX in tXs)
    //    ListBoxTXPool.Items.Add(new ListBoxItemTX(tX));
    //}

    void UpdateListBoxHeaderchain()
    {
      Header header = null;

      if (ListBoxBlockchain.Items.Count > 0)
        header = ((ListBoxItemHeader)ListBoxBlockchain.Items.GetItemAt(0)).Header;

      if (Token.HeaderTip != header)
        if (ListBoxBlockchain.Items.Count > 0 && Token.HeaderTip.HeaderPrevious == header)
          ListBoxBlockchain.Items.Insert(0, new ListBoxItemHeader(Token.HeaderTip));
        else
        {
          ListBoxBlockchain.Items.Clear();
          header = Token.HeaderTip;

          while (header != null)
          {
            ListBoxBlockchain.Items.Add(new ListBoxItemHeader(header));
            header = header.HeaderPrevious;
          }
        }
    }

    void ListBoxBlockchain_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      Header header = ((ListBoxItemHeader)ListBoxBlockchain.SelectedItem).Header;

      foreach (Window w in Application.Current.Windows)
      {
        DisplayHeaderWindow windowDisplayHeader = w as DisplayHeaderWindow;
        if (windowDisplayHeader != null && windowDisplayHeader.Header == header)
        {
          windowDisplayHeader.Activate();
          return;
        }
      }

      new DisplayHeaderWindow(header).Show();
    }

    void ButtonMakeTX_Click(object sender, RoutedEventArgs e)
    {
      Token.TryMineAnchorToken(
        out Token.TokenAnchor tokenAnchor,
        new byte[0],
        0);
    }
  }
}
