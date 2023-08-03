using System;
using System.Threading.Tasks;
using System.Windows;
using BTokenLib;

namespace BTokenWPF
{
  public partial class MainWindow : Window
  {
    TokenBToken BToken;

    public MainWindow()
    {
      InitializeComponent();

      BToken = new();
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

    void ButtonUpdateTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      TextBoxLog.Text += "new entry\n";
    }

    void ButtonClearTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      TextBoxLog.Text = "";
    }
  }
}
