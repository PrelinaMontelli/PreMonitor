using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;
using PreMonitor.Models;

namespace PreMonitor.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly string _configFilePath;
    private readonly string _settingsFilePath;
        private Dictionary<string, object> _configData = new();

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var preMonitorDir = Path.Combine(appData, "PreMonitor");
            if (!Directory.Exists(preMonitorDir))
            {
                Directory.CreateDirectory(preMonitorDir);
            }
            _settingsFilePath = Path.Combine(preMonitorDir, "settings.json");
            LoadConfiguration();
        }

        public T GetValue<T>(string key)
        {
            try
            {
                var keys = key.Split(':');
                var current = _configData;

                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (current.TryGetValue(keys[i], out var value) && value is Dictionary<string, object> dict)
                    {
                        current = dict;
                    }
                    else
                    {
                        return default!;
                    }
                }

                if (current.TryGetValue(keys[^1], out var finalValue))
                {
                    if (finalValue is JsonElement element)
                    {
                        return JsonSerializer.Deserialize<T>(element.GetRawText())!;
                    }
                    return (T)Convert.ChangeType(finalValue, typeof(T));
                }

                return default!;
            }
            catch (Exception ex)
            {
                _logger.LogError($"获取配置值失败 '{key}': {ex.Message}");
                return default!;
            }
        }

        public void SetValue(string key, object value)
        {
            try
            {
                var keys = key.Split(':');
                var current = _configData;

                // 导航到正确的嵌套字典
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (!current.TryGetValue(keys[i], out var existingValue) || existingValue is not Dictionary<string, object>)
                    {
                        current[keys[i]] = new Dictionary<string, object>();
                    }
                    current = (Dictionary<string, object>)current[keys[i]];
                }

                // 设置最终值
                current[keys[^1]] = value;
                _logger.LogInformation($"配置值已更新 '{key}': {value}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"设置配置值失败 '{key}': {ex.Message}");
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                    // 移除PropertyNamingPolicy，保持原始键名
                };

                var json = JsonSerializer.Serialize(_configData, options);
                await File.WriteAllTextAsync(_configFilePath, json);
                _logger.LogInformation("配置文件已保存");
            }
            catch (Exception ex)
            {
                _logger.LogError($"保存配置文件失败: {ex.Message}");
            }
        }

        public void Reload()
        {
            LoadConfiguration();
        }

        public PreMonitorSettings LoadPreMonitorSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<PreMonitorSettings>(json);
                    if (settings != null)
                    {
                        // 版本迁移点（预留）
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"加载 PreMonitor 设置失败: {ex.Message}");
            }

            // 返回默认配置
            return new PreMonitorSettings
            {
                Version = 1,
                GlobalThresholds = new Thresholds
                {
                    CpuPct = GetValue<double>("EdgeMonitoring:CpuThreshold") is double cpu && cpu > 0 ? cpu : 30.0,
                    MemoryMB = GetValue<long>("EdgeMonitoring:MemoryThresholdMB") is long mem && mem > 0 ? mem : 2048,
                    DiskBytesPerSec = 0,
                    GpuPct = 0,
                    NetBytesPerSec = 0,
                    SustainSeconds = 5
                },
                Rules = new List<ApplicationRule>()
            };
        }

        public async Task SavePreMonitorSettingsAsync(PreMonitorSettings settings)
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(settings, jsonOptions);
                await File.WriteAllTextAsync(_settingsFilePath, json);
                _logger.LogInformation($"PreMonitor 设置已保存到: {_settingsFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"保存 PreMonitor 设置失败: {ex.Message}");
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var jsonDoc = JsonDocument.Parse(json);
                    _configData = ConvertJsonElementToDictionary(jsonDoc.RootElement);
                }
                else
                {
                    _configData = new Dictionary<string, object>();
                }
                _logger.LogInformation("配置文件已加载");
            }
            catch (Exception ex)
            {
                _logger.LogError($"加载配置文件失败: {ex.Message}");
                _configData = new Dictionary<string, object>();
            }
        }

        private Dictionary<string, object> ConvertJsonElementToDictionary(JsonElement element)
        {
            var result = new Dictionary<string, object>();
            
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = ConvertJsonElementToObject(property.Value);
            }
            
            return result;
        }

        private object ConvertJsonElementToObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return ConvertJsonElementToDictionary(element);
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(ConvertJsonElementToObject).ToArray();
                case JsonValueKind.String:
                    return element.GetString() ?? "";
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetDouble(out double doubleValue))
                        return doubleValue;
                    return element.GetDecimal();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null!;
                default:
                    return element.ToString();
            }
        }
    }
}
