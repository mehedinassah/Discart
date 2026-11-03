using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic.FileIO;

namespace DiskAuditPro
{
    public partial class MainWindow : Window
    {
        private class Rule { public string id { get; set; } public string[] paths { get; set; } public string verdict { get; set; } public string reason { get; set; } }
        private class RulesConfig { public List<Rule> rules { get; set; } }
        private class ScanResult { public string Path { get; set; } public string Verdict { get; set; } public double GB { get; set; } public string Reason { get; set; } public bool IsFile { get; set; } }

        public MainWindow()
        {
            InitializeComponent();
            _ = ScanDriveAsync(); 
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e) 
        { 
            await ScanDriveAsync(); 
        }

        private async Task ScanDriveAsync()
        {
            // 1. Show Loading State
            ScanStatusText.Text = "Scanning... Please wait.";
            LoadingBar.Visibility = Visibility.Visible;
            ScanButton.IsEnabled = false;
            DeleteButton.IsEnabled = false;

            var drive = new DriveInfo("C");
            double totalGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            double freeGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            double usedGB = totalGB - freeGB;
            int percentUsed = (int)((usedGB / totalGB) * 100);

            DriveInfoText.Text = $"Drive C: {totalGB:F1} GB total | {freeGB:F1} GB free | {usedGB:F1} GB used ({percentUsed}%)";
            DriveProgressBar.Value = percentUsed;

            // 2. Do the heavy lifting in the background
            var results = await Task.Run(() =>
            {
                var res = new List<ScanResult>();
                if (File.Exists("rules.json"))
                {
                    string json = File.ReadAllText("rules.json");
                    var config = JsonSerializer.Deserialize<RulesConfig>(json);

                    foreach (var rule in config.rules)
                    {
                        foreach (var path in rule.paths)
                        {
                            string expandedPath = Environment.ExpandEnvironmentVariables(path);
                            if (Directory.Exists(expandedPath) || File.Exists(expandedPath))
                            {
                                double sizeGB = GetSizeInGB(expandedPath);
                                if (sizeGB > 0.05) 
                                {
                                    res.Add(new ScanResult
                                    {
                                        Path = expandedPath, Verdict = rule.verdict, GB = Math.Round(sizeGB, 2), Reason = rule.reason, IsFile = File.Exists(expandedPath)
                                    });
                                }
                            }
                        }
                    }
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Error: rules.json not found in the application folder!"));
                }
                return res;
            });

            // 3. Hide Loading State & Build UI
            BuildTree(results);
            ScanStatusText.Text = $"Scan complete. Found {results.Count} items.";
            
            LoadingBar.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
        }

        private double GetSizeInGB(string path)
        {
            if (File.Exists(path)) return new FileInfo(path).Length / (1024.0 * 1024.0 * 1024.0);
            try { return new DirectoryInfo(path).EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length) / (1024.0 * 1024.0 * 1024.0); }
            catch { return 0; }
        }

        private void BuildTree(List<ScanResult> results)
        {
            ResultsTree.Items.Clear();
            SelectionText.Text = "Nothing selected";

            var groups = new[] { "Safe", "ProbablySafe", "UserCall", "DontTouch" };
            var groupNames = new Dictionary<string, string> { { "Safe", "✅ Safe to delete" }, { "ProbablySafe", "🟡 Probably safe" }, { "UserCall", "🔵 Needs your call" }, { "DontTouch", "🔴 Don't touch" } };
            var colors = new Dictionary<string, string> { { "Safe", "#228B22" }, { "ProbablySafe", "#B48C00" }, { "UserCall", "#0078B4" }, { "DontTouch", "#A01E1E" } };

            foreach (var group in groups)
            {
                var items = results.Where(r => r.Verdict == group).OrderByDescending(r => r.GB).ToList();
                if (!items.Any()) continue;
                double groupTotal = items.Sum(i => i.GB);
                var groupNode = new TreeViewItem
                {
                    Header = $"{groupNames[group]} - {groupTotal:F1} GB",
                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colors[group]),
                    FontWeight = FontWeights.Bold, IsExpanded = group != "DontTouch"
                };

                foreach (var item in items)
                {
                    var checkBox = new CheckBox
                    {
                        Content = $"{item.GB:F1} GB  {item.Path}", Tag = item, IsChecked = group == "Safe", Margin = new Thickness(0, 2, 0, 2), ToolTip = item.Reason
                    };
                    checkBox.Checked += Item_Checked;
                    checkBox.Unchecked += Item_Checked;
                    groupNode.Items.Add(checkBox);
                }
                ResultsTree.Items.Add(groupNode);
            }
        }

        private void Item_Checked(object sender, RoutedEventArgs e)
        {
            double total = 0; int count = 0;
            foreach (TreeViewItem group in ResultsTree.Items)
                foreach (CheckBox cb in group.Items)
                    if (cb.IsChecked == true) { total += ((ScanResult)cb.Tag).GB; count++; }
            SelectionText.Text = count > 0 ? $"{count} item(s) selected - {total:F1} GB" : "Nothing selected";
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = new List<ScanResult>();
            foreach (TreeViewItem group in ResultsTree.Items)
                foreach (CheckBox cb in group.Items)
                    if (cb.IsChecked == true) toDelete.Add((ScanResult)cb.Tag);

            if (!toDelete.Any()) { MessageBox.Show("Nothing selected."); return; }

            if (MessageBox.Show($"Send {toDelete.Count} items ({toDelete.Sum(x => x.GB):F1} GB) to Recycle Bin?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                int failed = 0;
                foreach (var item in toDelete)
                {
                    try {
                        if (item.IsFile) FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    } catch { failed++; }
                }
                MessageBox.Show(failed > 0 ? $"Done. {failed} items failed." : "Successfully sent to Recycle Bin.");
                _ = ScanDriveAsync();
            }
        }
    }
}