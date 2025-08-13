namespace PreMonitor.Services
{
    /// <summary>
    /// 开机自启动服务接口
    /// </summary>
    public interface IStartupService
    {
        /// <summary>
        /// 检查是否已启用开机自启动
        /// </summary>
        /// <returns>如果已启用开机自启动则返回true</returns>
        bool IsStartupEnabled();

        /// <summary>
        /// 检查是否已启用管理员权限的开机自启动
        /// </summary>
        /// <returns>如果已启用管理员权限的开机自启动则返回true</returns>
        bool IsAdminStartupEnabled();

        /// <summary>
        /// 启用开机自启动
        /// </summary>
        /// <param name="runAsAdmin">是否以管理员权限启动</param>
        /// <returns>如果成功启用则返回true</returns>
        Task<bool> EnableStartupAsync(bool runAsAdmin = false);

        /// <summary>
        /// 启用开机后自动在托盘监测
        /// </summary>
        /// <returns>如果成功启用则返回true</returns>
        Task<bool> EnableTrayMonitorStartupAsync();

        /// <summary>
        /// 检查是否已启用开机后自动在托盘监测
        /// </summary>
        /// <returns>如果已启用则返回true</returns>
        bool IsTrayMonitorStartupEnabled();

        /// <summary>
        /// 禁用开机自启动
        /// </summary>
        /// <returns>如果成功禁用则返回true</returns>
        Task<bool> DisableStartupAsync();

        /// <summary>
        /// 检查启动任务状态并在需要时重新安排
        /// </summary>
        void StartupCheck();
    }
}
