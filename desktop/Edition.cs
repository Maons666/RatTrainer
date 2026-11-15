namespace RatTrainerDesktop
{
    internal static class Edition
    {
#if HOST_SANDBOX
        public static readonly bool HostOnly = true;
        public const string Key = "host";
        public const string Name = "房主沙盒版";
        public const string Subtitle = "把这间厨房，变成你的试验场。";
#else
        public static readonly bool HostOnly = false;
        public const string Key = "all";
        public const string Name = "全能版";
        public const string Subtitle = "小老鼠的大本领，都在这里。";
#endif
        public const string Version = "1.3.3";
    }
}
