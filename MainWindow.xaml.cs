using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Security.Principal;
using Microsoft.VisualBasic.FileIO;

namespace DiskAuditPro
{
    public partial class MainWindow : Window
    {
        private class Rule { public string id { get; set; } public string[] paths { get; set; } public string verdict { get; set; } public string reason { get; set; } public string article { get; set; } public string detect { get; set; } }
        private class RulesConfig { public string version { get; set; } public List<Rule> rules { get; set; } }
        private class ScanResult { public string Path { get; set; } public string Verdict { get; set; } public double GB { get; set; } public string Reason { get; set; } public string Article { get; set; } public bool IsFile { get; set; } }

        private TextBlock _currentlyFocusedTextBlock = null;
        private List<ScanResult> _lastScanResults = new List<ScanResult>();

        [System.Runtime.InteropServices.DllImport("Shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }

        [System.Runtime.InteropServices.DllImport("Shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public MainWindow()
        {
            InitializeComponent();
            CheckAdmin();
            _ = ScanDriveAsync(); 
            _ = CheckForRuleUpdatesAsync();
        }

        private void CheckAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    ModeText.Text = "✅ Running as Administrator";
                    ModeBanner.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // Light Green
                    AdminBtn.Content = "Restart as Standard User";
                    AdminBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    ModeText.Text = "⚠️ Running as Standard User";
                    ModeBanner.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Yellow
                    AdminBtn.Content = "Restart as Admin";
                    AdminBtn.Visibility = Visibility.Visible;
                }
            }
        }       

        private void AdminBtn_Click(object sender, RoutedEventArgs e)
        {
            var exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    // We are currently Admin, so open it normally (Standard)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exeName) { UseShellExecute = true });
                }
                else
                {
                    // We are currently Standard, so launch as Admin
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exeName) { UseShellExecute = true, Verb = "runas" });
                }
            }
            Application.Current.Shutdown();
        }
        private void HelpBtn_Click(object sender, RoutedEventArgs e)
        {
            string msg;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    msg = "You are running Discart as an Administrator.\n\nThis means Discart has full permission to read the exact size of your protected system files (like C:\\Windows and C:\\Program Files). It can also safely delete system-level caches.";
                }
                else
                {
                    msg = "You are running Discart as a Standard User.\n\nThis means Discart can safely scan your apps and personal files. However, Windows restricts it from measuring or deleting protected system files (like C:\\Windows). \n\nIf you want to see the exact size of your system folders, click 'Restart as Admin'.";
                }
            }
            MessageBox.Show(msg, "Discart - Permission Mode", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task CheckForRuleUpdatesAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                string url = "https://raw.githubusercontent.com/mehedinassah/Discart/main/rules.json";
                string remoteJson = await client.GetStringAsync(url);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var remoteConfig = JsonSerializer.Deserialize<RulesConfig>(remoteJson, options);

                if (File.Exists("rules.json"))
                {
                    var localConfig = JsonSerializer.Deserialize<RulesConfig>(File.ReadAllText("rules.json"), options);
                    if (remoteConfig.version != localConfig.version)
                    {
                        File.WriteAllText("rules.json", remoteJson);
                        if (!ScanStatusText.Text.StartsWith("Scan complete"))
                        {
                            ScanStatusText.Text = "Downloaded latest rules! Please Scan Again.";
                        }
                    }
                }
            } catch { }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e) { await ScanDriveAsync(); }

        private async Task ScanDriveAsync()
        {
            ScanStatusText.Text = "Scanning... Please wait.";
            LoadingBar.Visibility = Visibility.Visible;
            ScanButton.Content = "Scanning...";
            ScanButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            SweepBtn.IsEnabled = false;
            SearchBox.IsEnabled = false;
            ToggleAllBtn.IsEnabled = false;
            ExportBtn.IsEnabled = false;
            EmptyBinBtn.IsEnabled = false;
            
            ResultsPanel.Children.Clear();
            _currentlyFocusedTextBlock = null;
            CopyPathBtn.Visibility = Visibility.Collapsed;
            DetailsText.Text = "Select an item to see details. Expand a folder to see what's inside.";

            var loadingText = new TextBlock { 
                Text = "Scanning your drive...\nThis might take a few seconds.", 
                FontSize = 16, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = new Thickness(0, 100, 0, 0), TextAlignment = TextAlignment.Center
            };
            ResultsPanel.Children.Add(loadingText);

            var drive = new DriveInfo("C");
            double totalGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            double freeGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            double usedGB = totalGB - freeGB;
            int percentUsed = (int)((usedGB / totalGB) * 100);

            DriveInfoText.Text = $"Drive C: {totalGB:F1} GB total | {freeGB:F1} GB free | {usedGB:F1} GB used ({percentUsed}%)";
            DriveProgressBar.Value = percentUsed;

            _lastScanResults = await Task.Run(() =>
            {
                var res = new List<ScanResult>();
                if (File.Exists("rules.json"))
                {
                    string json = File.ReadAllText("rules.json");
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var config = JsonSerializer.Deserialize<RulesConfig>(json, options);

                    foreach (var rule in config.rules)
                    {
                        if (rule.detect == "pagefile") continue;
                        
                        foreach (var path in rule.paths)
                        {
                            string expandedPath = Environment.ExpandEnvironmentVariables(path);
                            
                            if (expandedPath.Contains("*"))
                            {
                                string dir = Path.GetDirectoryName(expandedPath);
                                string pattern = Path.GetFileName(expandedPath);
                                if (Directory.Exists(dir))
                                {
                                    foreach (var d in Directory.GetDirectories(dir, pattern))
                                    {
                                        double sizeGB = GetSizeInGB(d);
                                        if (sizeGB > 0.5) 
                                            res.Add(new ScanResult { Path = d, Verdict = rule.verdict, GB = Math.Round(sizeGB, 2), Reason = rule.reason, Article = rule.article, IsFile = false });
                                    }
                                }
                            }
                            else if (Directory.Exists(expandedPath) || File.Exists(expandedPath))
                            {
                                double sizeGB = GetSizeInGB(expandedPath);
                                if (sizeGB > 0.05 || rule.verdict == "DontTouch") 
                                {
                                    double finalSize = (sizeGB == 0 && rule.verdict == "DontTouch") ? -1 : Math.Round(sizeGB, 2);
                                    res.Add(new ScanResult { Path = expandedPath, Verdict = rule.verdict, GB = finalSize, Reason = rule.reason, Article = rule.article, IsFile = File.Exists(expandedPath) });
                                }
                            }
                        }
                    }
                }
                return res;
            });

            BuildUI(_lastScanResults);
            ScanStatusText.Text = $"Scan complete. Found {_lastScanResults.Count} items.";
            LoadingBar.Visibility = Visibility.Collapsed;
            ScanButton.Content = "Scan Again";
            ScanButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
            SweepBtn.IsEnabled = true;
            SearchBox.IsEnabled = true;
            ToggleAllBtn.IsEnabled = true;
            ExportBtn.IsEnabled = true;
            EmptyBinBtn.IsEnabled = true;
        }

        private double GetSizeInGB(string path)
        {
            if (File.Exists(path)) return new FileInfo(path).Length / (1024.0 * 1024.0 * 1024.0);
            try { return new DirectoryInfo(path).EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length) / (1024.0 * 1024.0 * 1024.0); }
            catch { return 0; }
        }

        private void BuildUI(List<ScanResult> results)
        {
            ResultsPanel.Children.Clear();
            var groups = new[] { "Safe", "ProbablySafe", "UserCall", "Suspicious", "DontTouch" };
            var groupNames = new Dictionary<string, string> { { "Safe", "✅ Safe to delete" }, { "ProbablySafe", "🟡 Probably safe" }, { "UserCall", "🔵 Needs your call" }, { "Suspicious", "🟠 Suspicious" }, { "DontTouch", "🔴 Don't touch" } };
            var colors = new Dictionary<string, string> { { "Safe", "#228B22" }, { "ProbablySafe", "#B48C00" }, { "UserCall", "#0078B4" }, { "Suspicious", "#D05200" }, { "DontTouch", "#A01E1E" } };

            string searchText = SearchBox.Text.ToLower();

            foreach (var group in groups)
            {
                var items = results.Where(r => r.Verdict == group && (string.IsNullOrWhiteSpace(searchText) || r.Path.ToLower().Contains(searchText))).OrderByDescending(r => r.GB).ToList();
                if (!items.Any()) continue;
                
                double groupTotal = items.Where(i => i.GB > 0).Sum(i => i.GB);
                string totalStr = group == "DontTouch" ? "Locked System Files" : $"{groupTotal:F1} GB";

                var groupText = new TextBlock { Text = $"{groupNames[group]} - {totalStr}", Foreground = (Brush)new BrushConverter().ConvertFromString(colors[group]), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) };
                ResultsPanel.Children.Add(groupText);

                foreach (var item in items)
                {
                    string sizeText = item.GB == -1 ? "Locked" : $"{item.GB:F1} GB";
                    var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 2, 0, 2) };

                    if (!item.IsFile && item.GB != -1) 
                    { 
                        var expandBtn = new Button { Content = "▶", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 10, Width = 20, Focusable = false };
                        expandBtn.Click += async (s, ev) => { await ToggleExpand(stackPanel, item, expandBtn); };
                        stackPanel.Children.Add(expandBtn);
                    }
                    else { stackPanel.Children.Add(new Border { Width = 20 }); }

                    var checkBox = new CheckBox { Tag = item, IsChecked = (group == "Safe"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsEnabled = group != "DontTouch" };
                    var textBlock = new TextBlock { Text = $"{sizeText}  {item.Path}", Tag = item, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, FontWeight = (group == "Safe") ? FontWeights.Bold : FontWeights.Normal };

                    checkBox.Checked += (s, ev) => { textBlock.FontWeight = FontWeights.Bold; UpdateSelectionSummary(); };
                    checkBox.Unchecked += (s, ev) => { textBlock.FontWeight = FontWeights.Normal; UpdateSelectionSummary(); };

                    textBlock.MouseLeftButtonDown += (s, ev) => {
                        if (_currentlyFocusedTextBlock != null) _currentlyFocusedTextBlock.TextDecorations = null;
                        if (_currentlyFocusedTextBlock == textBlock) { 
                            _currentlyFocusedTextBlock = null; 
                            DetailsText.Text = "Select an item to see details."; 
                            CopyPathBtn.Visibility = Visibility.Collapsed; 
                            CopyPathBtn.Tag = null;
                        }
                        else {
                            textBlock.TextDecorations = TextDecorations.Underline;
                            _currentlyFocusedTextBlock = textBlock;
                            DetailsText.Text = string.IsNullOrWhiteSpace(item.Article) ? item.Reason : item.Article;
                            CopyPathBtn.Tag = item.Path;
                            CopyPathBtn.Visibility = Visibility.Visible;
                        }
                    };

                    stackPanel.Children.Add(checkBox);
                    stackPanel.Children.Add(textBlock);
                    ResultsPanel.Children.Add(stackPanel);
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { BuildUI(_lastScanResults); }

        private void ToggleAllBtn_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnchecked = false;
            foreach (UIElement element in ResultsPanel.Children)
            {
                if (element is StackPanel sp && sp.Children[1] is CheckBox cb && cb.IsEnabled && cb.IsChecked == false) { anyUnchecked = true; break; }
            }

            foreach (UIElement element in ResultsPanel.Children)
            {
                if (element is StackPanel sp && sp.Children[1] is CheckBox cb && cb.IsEnabled) { cb.IsChecked = anyUnchecked; }
            }
        }

        private void SweepBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (UIElement element in ResultsPanel.Children)
            {
                if (element is StackPanel sp && sp.Children[1] is CheckBox cb && cb.Tag is ScanResult item && item.Verdict == "Safe") { cb.IsChecked = true; }
            }
            DeleteButton_Click(sender, e);
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string path = Path.Combine(desktop, $"Discart_Report_{DateTime.Now:yyyyMMdd_HHmm}.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Discart Report - {DateTime.Now}");
            sb.AppendLine("=========================================");
            foreach (var item in _lastScanResults.OrderByDescending(i => i.GB))
            {
                sb.AppendLine($"{item.GB:F1} GB | {item.Verdict} | {item.Path}");
            }
            File.WriteAllText(path, sb.ToString());
            MessageBox.Show($"Report saved to Desktop:\n{path}", "Export Complete");
        }

        private void EmptyBinBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to permanently empty the Recycle Bin?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    SHEmptyRecycleBin(IntPtr.Zero, null, 7);
                    MessageBox.Show("Recycle Bin emptied successfully.", "Discart");
                    _ = ScanDriveAsync();
                } catch { MessageBox.Show("Failed to empty Recycle Bin. You may need Administrator rights."); }
            }
        }

        private void CopyPathBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CopyPathBtn.Tag != null)
            {
                Clipboard.SetText(CopyPathBtn.Tag.ToString());
                MessageBox.Show("Path copied to clipboard!", "Discart");
            }
        }

        private async Task ToggleExpand(StackPanel parentPanel, ScanResult item, Button expandBtn)
        {
            int currentIndex = ResultsPanel.Children.IndexOf(parentPanel);
            if (currentIndex + 1 < ResultsPanel.Children.Count && ResultsPanel.Children[currentIndex + 1] is StackPanel nextPanel && nextPanel.Tag?.ToString() == item.Path)
            {
                ResultsPanel.Children.RemoveAt(currentIndex + 1);
                expandBtn.Content = "▶"; 
                return;
            }

            expandBtn.Content = "▼"; 
            var subItems = await Task.Run(() =>
            {
                var list = new List<ScanResult>();
                try
                {
                    var dirInfo = new DirectoryInfo(item.Path);
                    foreach (var d in dirInfo.GetDirectories().OrderByDescending(d => GetSizeInGB(d.FullName)).Take(5))
                    {
                        double gb = GetSizeInGB(d.FullName);
                        if (gb > 0) list.Add(new ScanResult { Path = d.FullName, GB = Math.Round(gb, 2), Reason = "Subfolder", Article = "This is a subfolder inside the parent directory.", IsFile = false, Verdict = item.Verdict });
                    }
                    foreach (var f in dirInfo.GetFiles().OrderByDescending(f => f.Length).Take(5))
                    {
                        double gb = f.Length / (1024.0 * 1024.0 * 1024.0);
                        if (gb > 0.01) list.Add(new ScanResult { Path = f.FullName, GB = Math.Round(gb, 2), Reason = "File", Article = "This is a file. (Read-only in Discart - delete via File Explorer)", IsFile = true, Verdict = item.Verdict });
                    }
                } catch { }
                return list.OrderByDescending(x => x.GB).ToList();
            });

            var container = new StackPanel { Tag = item.Path, Margin = new Thickness(45, 0, 0, 0) };
            
            if (subItems.Count == 0) { container.Children.Add(new TextBlock { Text = "Folder is empty or access denied.", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic }); }
            else
            {
                foreach (var sub in subItems)
                {
                    string typeLabel = sub.IsFile ? "(File)" : "(Folder)";
                    var text = new TextBlock { Text = $"{sub.GB:F1} GB  {Path.GetFileName(sub.Path)}  {typeLabel}", Tag = sub, Margin = new Thickness(0, 2, 0, 2), ToolTip = sub.Path, Foreground = Brushes.Gray, Cursor = Cursors.Hand };
                    text.MouseLeftButtonDown += (s, ev) => {
                        if (_currentlyFocusedTextBlock != null) _currentlyFocusedTextBlock.TextDecorations = null;
                        if (_currentlyFocusedTextBlock == text) { _currentlyFocusedTextBlock = null; DetailsText.Text = "Select an item to see details."; CopyPathBtn.Visibility = Visibility.Collapsed; CopyPathBtn.Tag = null; }
                        else { 
                            text.TextDecorations = TextDecorations.Underline; 
                            _currentlyFocusedTextBlock = text; 
                            DetailsText.Text = sub.Article; 
                            CopyPathBtn.Tag = sub.Path;
                            CopyPathBtn.Visibility = Visibility.Visible;
                        }
                    };
                    container.Children.Add(text);
                }
            }
            ResultsPanel.Children.Insert(currentIndex + 1, container);
        }

        private void UpdateSelectionSummary()
        {
            double total = 0; int count = 0;
            foreach (UIElement element in ResultsPanel.Children)
            {
                if (element is StackPanel sp && sp.Children[1] is CheckBox cb && cb.IsChecked == true) { total += ((ScanResult)cb.Tag).GB; count++; }
            }
            SelectionText.Text = count > 0 ? $"{count} item(s) selected - {total:F1} GB" : "Nothing selected";
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = new List<ScanResult>();
            foreach (UIElement element in ResultsPanel.Children)
            {
                if (element is StackPanel sp && sp.Children[1] is CheckBox cb && cb.IsChecked == true) toDelete.Add((ScanResult)cb.Tag);
            }

            if (!toDelete.Any()) { MessageBox.Show("Nothing selected."); return; }

            if (MessageBox.Show($"Send {toDelete.Count} items ({toDelete.Sum(x => x.GB):F1} GB) to Recycle Bin?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                int failed = 0; int success = 0;
                ushort flags = 0x40 | 0x10 | 0x4 | 0x400 | 0x200;

                foreach (var item in toDelete)
                {
                    try {
                        SHFILEOPSTRUCT fileop = new SHFILEOPSTRUCT { wFunc = 3, pFrom = item.Path + '\0' + '\0', fFlags = flags };
                        int result = SHFileOperation(ref fileop);
                        if (result == 0 && !fileop.fAnyOperationsAborted) success++; else failed++;
                    } catch { failed++; }
                }
                
                if (failed > 0) MessageBox.Show($"Sent {success} items to Recycle Bin.\n{failed} items were skipped because they are currently in use by Windows.", "Discart");
                else MessageBox.Show("Successfully sent all selected items to Recycle Bin.", "Discart");
                _ = ScanDriveAsync();
            }
        }
    }
}