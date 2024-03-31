using BepInEx.Logging;
using DoubleSidedDoors.BepInEx;

namespace DoubleSidedDoors.BepInEx {
    // REMEMBER TO SET THESE => otherwise program just wont work lmao
    public static class Module {
        public const string GUID = "randomuserhi.DoubleSidedDoors";
        public const string Name = "DoubleSidedDoors";
        public const string Version = "0.0.1";
    }
}

namespace API {
    /* Ripped from GTFO-API */
    internal static class APILogger {
        private static readonly ManualLogSource logger;

        static APILogger() {
            logger = new ManualLogSource("Rand-API");
            Logger.Sources.Add(logger);
        }
        private static string Format(string module, object msg) => $"[{module}]: {msg}";

        public static void Info(string module, object data) => logger.LogMessage(Format(module, data));
        public static void Verbose(string module, object data) {
#if DEBUG
            logger.LogDebug(Format(module, data));
#endif
        }
        public static void Log(object data) {
            logger.LogDebug(Format(Module.Name, data));
        }
        public static void Debug(object data, bool force = false) {
            if (ConfigManager.Debug || force) Log(data);
        }
        public static void Warn(object data) => logger.LogWarning(Format(Module.Name, data));
        public static void Error(object data) => logger.LogError(Format(Module.Name, data));
    }
}