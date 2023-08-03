using System;
using System.Threading.Tasks;
using System.Windows;

namespace BTokenWPF
{
  public partial class MainWindow : Window, BTokenLib.ILogEntryNotifier
  {
    BTokenLib.TokenBToken BToken;

    public MainWindow()
    {
      InitializeComponent();

      BToken = new(this);
      BToken.Start();

      UpdateTextBoxStatus();
    }

    async Task UpdateTextBoxStatus()
    {
      try
      {
        while (true)
        {
          await Task.Delay(500);
          TextBoxStatus.Text = BToken.GetStatus();
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

    public void NotifyLogEntry(string logEntry, string source)
    {
      lock (TextBoxLog)
        TextBoxLog.Text += $"{logEntry}\n";
    }

    void ButtonClearTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      TextBoxLog.Text = "";
    }
  }
}
