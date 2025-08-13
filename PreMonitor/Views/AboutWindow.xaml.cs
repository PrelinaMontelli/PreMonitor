using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Navigation;
using PreMonitor.Services;

namespace PreMonitor.Views
{
    public partial class AboutWindow : Window
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public AboutWindow()
        {
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            // 使用版本辅助类自动获取版本号
            VersionTextBlock.Text = VersionHelper.GetVersionString();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 禁用按钮防止重复点击
                var button = sender as System.Windows.Controls.Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "检查中...";
                }

                // 配置HttpClient请求头
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "PreMonitor/2.0.0");
                
                // 从GitHub API获取最新release信息
                string apiUrl = "https://api.github.com/repos/PrelinaMontelli/PreMonitor/releases/latest";
                
                var response = await _httpClient.GetAsync(apiUrl);
                
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "检查更新";
                }

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var releaseInfo = JsonDocument.Parse(responseContent);
                    
                    // 优先使用 name 字段，如果不可用则使用 tag_name
                    var latestVersionFromName = releaseInfo.RootElement.GetProperty("name").GetString();
                    var latestVersionFromTag = releaseInfo.RootElement.GetProperty("tag_name").GetString();
                    var currentVersion = VersionHelper.GetVersionString();

                    // 尝试从 name 字段提取版本号（如 "EdgeMonitor-v1.3" -> "v1.3"）
                    string? latestVersion = ExtractVersionFromReleaseName(latestVersionFromName) ?? latestVersionFromTag;

                    // 比较版本号
                    if (!string.IsNullOrEmpty(latestVersion) && IsNewerVersion(latestVersion, currentVersion))
                    {
                        var result = MessageBox.Show(
                            $"发现新版本: {latestVersion}\n当前版本: {currentVersion}\n\n是否前往下载页面？",
                            "发现更新",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            // 打开GitHub release页面
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://github.com/PrelinaMontelli/PreMonitor/releases/tag/Release",
                                UseShellExecute = true
                            });
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            $"当前已是最新版本: {currentVersion}\n(最新发布: {latestVersionFromName ?? latestVersionFromTag})",
                            "检查更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    // API请求失败，提供备用方案
                    var result = MessageBox.Show(
                        $"无法自动检查更新（HTTP {(int)response.StatusCode}）\n\n是否直接打开GitHub发布页面查看最新版本？",
                        "检查更新",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/PrelinaMontelli/PreMonitor/releases",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (sender is System.Windows.Controls.Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "检查更新";
                }

                // 提供备用方案
                var result = MessageBox.Show(
                    $"检查更新失败: {ex.Message}\n\n是否直接打开GitHub发布页面查看最新版本？",
                    "错误",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/PrelinaMontelli/PreMonitor/releases",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception browserEx)
                    {
                        MessageBox.Show(
                            $"无法打开浏览器: {browserEx.Message}\n\n请手动访问: https://github.com/PrelinaMontelli/PreMonitor/releases",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        }

        private string? ExtractVersionFromReleaseName(string? releaseName)
        {
            if (string.IsNullOrEmpty(releaseName)) return null;

            try
            {
                // 尝试匹配各种可能的版本号格式
                var patterns = new[]
                {
                    @"PreMonitor-v(\d+\.\d+(?:\.\d+)?)",     // PreMonitor-v1.3 或 PreMonitor-v1.3.0
                    @"EdgeMonitor-v(\d+\.\d+(?:\.\d+)?)",    // 历史兼容
                    @"EM-v(\d+\.\d+(?:\.\d+)?)",             // EM-v1.4 或 EM-v1.4.0
                    @"Edge\s*Monitor-v(\d+\.\d+(?:\.\d+)?)", // 历史兼容：Edge Monitor-v1.3 (带空格)
                    @"[A-Za-z\s]*-v(\d+\.\d+(?:\.\d+)?)",    // 任意前缀-v1.3 (通用匹配)
                    @"v(\d+\.\d+(?:\.\d+)?)",                // v1.3 或 v1.3.0
                    @"(\d+\.\d+(?:\.\d+)?)"                  // 1.3 或 1.3.0 (纯数字)
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(releaseName, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return "v" + match.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // 解析失败时返回null
            }

            return null;
        }

        private bool IsNewerVersion(string? latestVersion, string currentVersion)
        {
            if (string.IsNullOrEmpty(latestVersion)) return false;

            try
            {
                // 移除版本号中的 'v' 前缀
                var latest = latestVersion.TrimStart('v');
                var current = currentVersion.TrimStart('v');

                var latestParts = latest.Split('.').Select(int.Parse).ToArray();
                var currentParts = current.Split('.').Select(int.Parse).ToArray();

                // 确保两个版本号都有相同的部分数量
                var maxLength = Math.Max(latestParts.Length, currentParts.Length);
                var latestExpanded = latestParts.Concat(Enumerable.Repeat(0, maxLength - latestParts.Length)).ToArray();
                var currentExpanded = currentParts.Concat(Enumerable.Repeat(0, maxLength - currentParts.Length)).ToArray();

                for (int i = 0; i < maxLength; i++)
                {
                    if (latestExpanded[i] > currentExpanded[i])
                        return true;
                    if (latestExpanded[i] < currentExpanded[i])
                        return false;
                }

                return false; // 版本号相同
            }
            catch
            {
                return false; // 解析失败时假设没有新版本
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法打开链接: {ex.Message}\n\n链接地址: {e.Uri.AbsoluteUri}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
