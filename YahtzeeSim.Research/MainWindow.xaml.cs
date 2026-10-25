using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace YahtzeeSim.Research;

public partial class MainWindow : Window
{
    private readonly ResearchSession? _session;
    private ResearchSnapshot? _snapshot;
    private CancellationTokenSource? _pull;
    private bool _viewerReady;
    private string? _shownHtml;

    public MainWindow()
    {
        InitializeComponent();
        string? root = ResearchSession.FindRepoRoot();
        if (root == null)
        {
            Status.Text = "Could not find YahtzeeSim.sln. Run the app from inside the repository.";
            PullButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
            return;
        }

        _session = new ResearchSession(root);
        Loaded += OnLoaded;
        Closing += (_, _) => _pull?.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Viewer.EnsureCoreWebView2Async();
            Viewer.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _viewerReady = true;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or COMException)
        {
            Status.Text = "The WebView2 runtime is missing. Install it from Microsoft to see the reports.";
            AppendLog(ex.ToString());
            return;
        }

        ShowHtml(Message("Pull the latest data, or re-run the analysis on the current export."));
        if (_session!.HasExport)
        {
            await AnalyzeAsync();
        }
        else
        {
            Status.Text = "No export yet. Click Pull latest data.";
        }
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null)
        {
            return;
        }

        SetBusy(true, "Pulling data from the database…");
        LogExpander.IsExpanded = true;
        _pull = new CancellationTokenSource();
        try
        {
            int exit = await _session.PullAsync(line => Dispatcher.Invoke(() => AppendLog(line)), _pull.Token);
            if (exit != 0)
            {
                SetBusy(false, $"The export failed (exit code {exit}). See the log; you may need 'npx wrangler login'.");
                return;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            SetBusy(false, "The export could not run: " + ex.Message);
            return;
        }

        await AnalyzeAsync();
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null)
        {
            return;
        }

        string folder = Directory.Exists(_session.ReportsDir) ? _session.ReportsDir : _session.ExportsDir;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private async Task AnalyzeAsync()
    {
        if (_session == null)
        {
            return;
        }

        if (!_session.HasExport)
        {
            Status.Text = "No export yet. Click Pull latest data.";
            return;
        }

        SetBusy(true, "Analysing…");
        string? selected = (PlayerList.SelectedItem as ResearchPlayer)?.Slug;
        try
        {
            _snapshot = await Task.Run(() => _session.Analyze(line => Dispatcher.Invoke(() => AppendLog(line))));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetBusy(false, "Analysis failed: " + ex.Message);
            AppendLog(ex.ToString());
            return;
        }

        PlayerList.ItemsSource = _snapshot.Players;
        PlayerList.SelectedItem = _snapshot.Players.FirstOrDefault(p => p.Slug == selected) ?? _snapshot.Players.FirstOrDefault();
        ShowCurrent();
        SetBusy(false, $"{_snapshot.Players.Count} players · {_snapshot.Skipped} games skipped · updated {_snapshot.BuiltAt:t} · reports also saved to exports\\analysis");
    }

    private void PlayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a different player while on the overview jumps to their story.
        if (OverviewTab.IsChecked == true && e.AddedItems.Count > 0 && e.RemovedItems.Count > 0)
        {
            StoryTab.IsChecked = true;
        }

        ShowCurrent();
    }

    private void ViewTab_Checked(object sender, RoutedEventArgs e) => ShowCurrent();

    private void ShowCurrent()
    {
        if (!_viewerReady || _snapshot == null)
        {
            return;
        }

        var player = PlayerList.SelectedItem as ResearchPlayer;
        string html = OverviewTab.IsChecked == true || player == null
            ? _snapshot.OverviewHtml
            : ReportTab.IsChecked == true ? player.ReportHtml
            : StoryTab.IsChecked == true ? player.StoryHtml
            : player.BadgeHtml;
        ShowHtml(html);
    }

    private void ShowHtml(string html)
    {
        if (!_viewerReady || ReferenceEquals(html, _shownHtml))
        {
            return;
        }

        _shownHtml = html;
        Viewer.NavigateToString(html);
    }

    /// <summary>Player cards on the overview link to "slug.html": show that player's full report instead of navigating.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:", StringComparison.Ordinal) || e.Uri.StartsWith("about:", StringComparison.Ordinal))
        {
            return;
        }

        e.Cancel = true;
        string name = Path.GetFileNameWithoutExtension(new Uri(e.Uri).AbsolutePath);
        var player = _snapshot?.Players.FirstOrDefault(p => p.Slug == name);
        if (player != null)
        {
            ReportTab.IsChecked = true;
            PlayerList.SelectedItem = player;
            ShowCurrent();
        }
    }

    private void SetBusy(bool busy, string status)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PullButton.IsEnabled = !busy;
        AnalyzeButton.IsEnabled = !busy;
        Status.Text = status;
    }

    private void AppendLog(string line)
    {
        Log.AppendText(line + Environment.NewLine);
        Log.ScrollToEnd();
    }

    private static string Message(string text) =>
        $"<!doctype html><html><body style=\"font:15px Segoe UI,sans-serif;color:#56645c;padding:40px;background:#f1efe6\">{System.Net.WebUtility.HtmlEncode(text)}</body></html>";
}
