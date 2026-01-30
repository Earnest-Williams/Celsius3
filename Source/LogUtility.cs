namespace Celsius
{
    enum LogLevel
    {
        Message = 0,
        Important,
        Warning,
        Error
    };

    static class LogUtility
    {
        internal static void Log(string message, LogLevel logLevel = LogLevel.Message)
        {
            message = $"[Celsius] {message}";
            switch (logLevel)
            {
                case LogLevel.Message:
                    if (Settings.DebugMode)
                        Verse.Log.Message(message);
                    break;

                case LogLevel.Important:
                    Verse.Log.Message(message);
                    break;

                case LogLevel.Warning:
                    Verse.Log.Warning(message);
                    break;

                case LogLevel.Error:
                    Verse.Log.Error(message);
                    break;
            }
        }
    }

    class LogContext {
        string _ctx;

        public LogContext(string ctx) { _ctx = $"[Celsius.{ctx}]"; }

        public void Spam(string msg) { if (Settings.Spam) Verse.Log.Message(_ctx + " " + msg); }
        public void Info(string msg) { Verse.Log.Message(_ctx + " " + msg); }
        public void Warn(string msg) { Verse.Log.Warning(_ctx + " " + msg); }
        public void WarnOnce(int key, string msg) { Verse.Log.WarningOnce(_ctx + " " + msg, key); }
        public void Error(string msg) { Verse.Log.Error(_ctx + " " + msg); }
        public void ErrOnce(int key, string msg) { Verse.Log.ErrorOnce(_ctx + " " + msg, key); }
    }

    static class LC {
        public static LogContext Info(string ctx, string msg) {
            var log = new LogContext(ctx);
            log.Info(msg);
            return log;
        }
        public static LogContext Error(string ctx, string msg) {
            var log = new LogContext(ctx);
            log.Error(msg);
            return log;
        }
        public static LogContext Warn(string ctx, string msg) {
            var log = new LogContext(ctx);
            log.Warn(msg);
            return log;
        }
    }
}
