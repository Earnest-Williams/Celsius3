namespace Celsius
{
    class LogContext {
        string _ctx;

        public LogContext(string ctx) { _ctx = $"[Celsius.{ctx}]"; }

        public void Spam(string msg) { if (Settings.DebugMode) Verse.Log.Message(_ctx + " " + msg); }
        public void Info(string msg) { Verse.Log.Message(_ctx + " " + msg); }
        public void Warn(string msg) { Verse.Log.Warning(_ctx + " " + msg); }
        public void WarnOnce(int key, string msg) { Verse.Log.WarningOnce(_ctx + " " + msg, key); }
        public void Error(string msg) { Verse.Log.Error(_ctx + " " + msg); }
        public void ErrOnce(int key, string msg) { Verse.Log.ErrorOnce(_ctx + " " + msg, key); }
    }

    static class LC {
        public static LogContext Spam(string ctx, string msg) {
            var log = new LogContext(ctx);
            if (Settings.DebugMode) { log.Info(msg); }
            return log;
        }
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
