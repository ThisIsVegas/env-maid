using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace EnvMaid.App.Views;

public partial class AboutDialog : Window
{
    private const string RepositoryUrl = "https://github.com/ThisIsVegas/env-maid";
    private const string NewIssueUrl = $"{RepositoryUrl}/issues/new";

    public string VersionText { get; }

    public AboutDialog()
    {
        VersionText = $"Version {GetDisplayVersion()}";
        DataContext = this;
        InitializeComponent();
    }

    private static string GetDisplayVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return informationalVersion?.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "Unknown";
    }

    private void ViewSource_Click(object sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);

    private void ReportIssue_Click(object sender, RoutedEventArgs e) => OpenUrl(NewIssueUrl);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
