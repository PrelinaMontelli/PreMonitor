using System.Reflection;

namespace PreMonitor.Services
{
    public static class VersionHelper
    {
        /// <summary>
        /// 获取当前应用程序的版本号
        /// </summary>
        /// <returns>格式化的版本号字符串</returns>
        public static string GetVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                // 如果构建版本是0，只显示主版本.次版本
                if (version.Build == 0)
                {
                    return $"v{version.Major}.{version.Minor}";
                }
                // 否则显示主版本.次版本.构建版本
                return $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            return "v1.0";
        }

        /// <summary>
        /// 获取完整的版本信息
        /// </summary>
        /// <returns>完整版本号</returns>
        public static string GetFullVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "1.0.0.0";
        }

        /// <summary>
        /// 获取程序集文件版本
        /// </summary>
        /// <returns>文件版本号</returns>
        public static string GetFileVersionString()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            return fileVersionInfo.FileVersion ?? "1.0.0.0";
        }
    }
}
