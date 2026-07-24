using System.Windows;

namespace EnvMaid.App.Views;

public partial class PathInputDialog : Window
{
    public string? ResultPath { get; private set; }

    public PathInputDialog(string title, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = title;
        InputBox.Text = initialValue;
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultPath = InputBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
