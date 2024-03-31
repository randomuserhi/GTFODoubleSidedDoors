using System.Text.Json;

namespace DoubleSidedDoors {
    internal static class JSON {
        private static readonly JsonSerializerOptions _setting;

        static JSON() {
            _setting = new JsonSerializerOptions {
                ReadCommentHandling = JsonCommentHandling.Skip,
                IncludeFields = false,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                IgnoreReadOnlyProperties = true
            };
        }

        public static T Deserialize<T>(string json) {
            return JsonSerializer.Deserialize<T>(json, _setting)!;
        }

        public static object Deserialize(Type type, string json) {
            return JsonSerializer.Deserialize(json, type, _setting)!;
        }

        public static string Serialize(object value, Type type) {
            return JsonSerializer.Serialize(value, type, _setting);
        }
    }
}
