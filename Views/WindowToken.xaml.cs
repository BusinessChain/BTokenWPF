using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
      TextBoxToken.Text = token.GetName().Substring("Token".Count());

      UpdateControlsWindowToken();
    }

    async Task UpdateControlsWindowToken()
    {
      try
      {
        while (true)
        {
          if (Token.TryLock())
          {
            UpdateListBoxHeaderchain();

            UpdateTextBoxWallet();

            UpdateListBoxTXPool();

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

      TextBoxCountPeers.Text = $"Number of connected peers: {peers.Count}";
      TextBoxFlagEnableInboundConnections.Text = $"Inbound connections enabled: {Token.Network.EnableInboundConnections}";
      TextBoxStateNetwork.Text = $"Network state: {Token.Network.State}";

      ListBoxPeers.Items.Clear();

      foreach (Network.Peer peer in peers)
        ListBoxPeers.Items.Add(new ListBoxItemPeer(peer));
    }

    Header HeaderTipAtLastUpdate;
    void UpdateTextBoxWallet()
    {
      if (HeaderTipAtLastUpdate == Token.HeaderTip)
        return;

      HeaderTipAtLastUpdate = Token.HeaderTip;

      ListBoxWallet.Items.Clear();
      ListBoxWallet.Items.Add(new ListBoxItemWallet());

      foreach (TXOutputWallet tXOutputWallet in Token.Wallet.OutputsValueDesc)
        ListBoxWallet.Items.Add(new ListBoxItemWallet(tXOutputWallet));
    }

    int CountTXsPoolOld;
    void UpdateListBoxTXPool()
    {
      int countTXsPool = Token.TXPool.GetCountTXs();

      if (countTXsPool == CountTXsPoolOld)
        return;

      List<TX> tXs = Token.TXPool.GetTXs(out CountTXsPoolOld);

      ListBoxTXPool.Items.Clear();

      foreach (TX tX in tXs)
        ListBoxTXPool.Items.Add(new ListBoxItemTX(tX));
    }

    void UpdateListBoxHeaderchain()
    {
      Header header = null;

      if (ListBoxHeaderchain.Items.Count > 0)
        header = ((ListBoxItemHeader)ListBoxHeaderchain.Items.GetItemAt(0)).Header;

      if (Token.HeaderTip != header)
        if (ListBoxHeaderchain.Items.Count > 0 && Token.HeaderTip.HeaderPrevious == header)
          ListBoxHeaderchain.Items.Insert(0, new ListBoxItemHeader(Token.HeaderTip));
        else
        {
          ListBoxHeaderchain.Items.Clear();
          header = Token.HeaderTip;

          while (header != null)
          {
            ListBoxHeaderchain.Items.Add(new ListBoxItemHeader(header));
            header = header.HeaderPrevious;
          }
        }
    }

    private void ListBoxHeaderchain_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      Header header = ((ListBoxItemHeader)ListBoxHeaderchain.SelectedItem).Header;

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
  }
}
