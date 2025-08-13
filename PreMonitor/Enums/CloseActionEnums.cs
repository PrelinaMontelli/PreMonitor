namespace PreMonitor
{
    /// <summary>
    /// 窗口关闭行为枚举
    /// </summary>
    public enum CloseAction
    {
        /// <summary>
        /// 每次询问用户
        /// </summary>
        Ask,
        
        /// <summary>
        /// 最小化到托盘
        /// </summary>
        MinimizeToTray,
        
        /// <summary>
        /// 直接退出程序
        /// </summary>
        Exit
    }

    /// <summary>
    /// 关闭选项对话框的选择结果
    /// </summary>
    public enum CloseOption
    {
        /// <summary>
        /// 取消关闭
        /// </summary>
        Cancel,
        
        /// <summary>
        /// 最小化到托盘
        /// </summary>
        MinimizeToTray,
        
        /// <summary>
        /// 退出程序
        /// </summary>
        Exit
    }
}
