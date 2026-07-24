using System.Windows;

namespace EnvMaid.App.Views;

public partial class TextListDialog : Window
{
    public TextListDialog(string header, string body)
    {
        InitializeComponent();
        Title = header;
        HeaderText.Text = header;
        Body.Text = body;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
