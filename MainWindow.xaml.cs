using BTokenLib;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BTokenWPF
{
  public partial class MainWindow : Window, ILogEntryNotifier
  {
    TokenBToken BToken;

    public MainWindow()
    {
      try
      {
        InitializeComponent();

        BToken = new(this);
        BToken.Start();

        UpdateTextBoxStatus();
      }
      catch(Exception ex)
      {
        MessageBox.Show(
          $"{ex.Message}: {ex.StackTrace}",
          $"{ex.GetType().Name}",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
    }

    async Task UpdateTextBoxStatus()
    {
      try
      {
        while (true)
        {
          await Task.Delay(1000);
          LabelBitcoinStatus.Content = BToken.TokenParent.GetStatus();
          LabelBTokenStatus.Content = BToken.GetStatus();
        }
      }
      catch(Exception ex)
      {
        MessageBox.Show(
          $"{ex.Message}: {ex.StackTrace}",
          $"{ex.GetType().Name}",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
    }

    readonly object LOCK_Dispatcher = new();
    public void NotifyLogEntry(string logEntry, string source)
    {
      lock (LOCK_Dispatcher)
        Dispatcher.Invoke(() =>
        {
          TextBoxLog.SelectionStart = 0;
          TextBoxLog.SelectionLength = 0;
          TextBoxLog.SelectedText = $"{logEntry}\n";
        });
    }

    void ButtonClearTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      TextBoxLog.Text = "";
    }

    void ButtonBitcoinMiner_Click(object sender, RoutedEventArgs e)
    {
      if (BToken.TokenParent.IsMining)
      {
        BToken.TokenParent.StopMining();
        ButtonStartBitcoinMiner.Content = "Start BitcoinMiner";
      }
      else
      {
        BToken.TokenParent.StartMining();
        ButtonStartBitcoinMiner.Content = "Stop BitcoinMiner";
      }
    }
    void ButtonBTokenMiner_Click(object sender, RoutedEventArgs e)
    {
      if (BToken.IsMining)
      {
        BToken.StopMining();
        ButtonStartBTokenMiner.Content = "Start BTokenMiner";
      }
      else
      {
        BToken.StartMining();
        ButtonStartBTokenMiner.Content = "Stop BTokenMiner";
      }
    }
    void ButtonStartSynchronizationBitcoin_Click(object sender, RoutedEventArgs e)
    {
      BToken.TokenParent.Network.TryStartSynchronization();
    }
    void ButtonStartSynchronizationBToken_Click(object sender, RoutedEventArgs e)
    {
      BToken.Network.TryStartSynchronization();
    }

    void ButtonOpenBitcoinWindow_Click(object sender, RoutedEventArgs e)
    {
      OpenWindowToken(BToken.TokenParent);
    }
    void ButtonOpenBTokenWindow_Click(object sender, RoutedEventArgs e)
    {
      OpenWindowToken(BToken);
    }
    void OpenWindowToken(Token token)
    {
      foreach (Window w in Application.Current.Windows)
      {
        WindowToken windowToken = w as WindowToken;
        if (windowToken != null && windowToken.Token == token)
        {
          windowToken.Activate();
          return;
        }
      }

      new WindowToken(token).Show();
    }

    private void PrintChainsToFile_Click(object sender, RoutedEventArgs e)
    {
      string textImage = "";
      BToken.PrintImage(ref textImage);
      File.WriteAllText("printImage.txt", textImage);

      string textBlocks = "";
      BToken.PrintBlocks(ref textBlocks);
      File.WriteAllText("printBlocks.txt", textBlocks);
    }
  }
}
