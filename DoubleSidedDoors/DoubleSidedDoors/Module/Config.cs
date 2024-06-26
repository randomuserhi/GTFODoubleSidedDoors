﻿using API;
using BepInEx;
using BepInEx.Configuration;
using DoubleSidedDoors.Patches;
using MTFO.API;

namespace DoubleSidedDoors.BepInEx {
    public static partial class ConfigManager {
        public static ConfigFile configFile;

        static ConfigManager() {
            string text = Path.Combine(Paths.ConfigPath, $"{Module.Name}.cfg");
            configFile = new ConfigFile(text, true);

            debug = configFile.Bind(
                "Debug",
                "enable",
                false,
                "Enables debug messages when true.");

            DirectoryInfo dir = Directory.CreateDirectory(Path.Combine(MTFOPathAPI.CustomPath, "DoubleSidedDoors"));
            FileInfo[] files = dir.GetFiles();
            APILogger.Debug($"Searching: {dir.FullName}", true);
            foreach (FileInfo fileInfo in files) {
                APILogger.Debug($"Found: {fileInfo.FullName}", true);
                string extension = fileInfo.Extension;
                bool flag = extension.Equals(".json", StringComparison.InvariantCultureIgnoreCase);
                bool flag2 = extension.Equals(".jsonc", StringComparison.InvariantCultureIgnoreCase);
                if (flag || flag2) {
                    LayoutConfig layoutConfig = JSON.Deserialize<LayoutConfig>(File.ReadAllText(fileInfo.FullName));
                    if (Spawn.data.ContainsKey(layoutConfig.LevelLayoutID)) {
                        APILogger.Error($"Duplicated ID found!: {fileInfo.Name}, {layoutConfig.LevelLayoutID}");
                    } else {
                        Spawn.data.Add(layoutConfig.LevelLayoutID, layoutConfig);
                    }
                }
            }
        }

        public static bool Debug {
            get { return debug.Value; }
            set { debug.Value = value; }
        }
        private static ConfigEntry<bool> debug;
    }
}