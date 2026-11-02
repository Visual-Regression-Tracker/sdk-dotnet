using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualRegressionTracker
{
    public class Config
    {
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new StringOrNumberConverter(), new BooleanLikeConverter() }
        };
        public static readonly string DefaultPath = "vrt.json";

        [JsonPropertyName("apiUrl")]
        public string ApiUrl { get; set; } = "http://localhost:4200";
        [JsonPropertyName("ciBuildId")]
        public string CiBuildId { get; set; }
        [JsonPropertyName("branchName")]
        public string BranchName { get; set; }
        [JsonPropertyName("project")]
        public string Project { get; set; }
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; }
        [JsonPropertyName("enableSoftAssert")]
        public bool EnableSoftAssert { get; set; }

        public void CheckComplete()
        {
            if (string.IsNullOrEmpty(ApiUrl)) {
                throw new MissingConfigurationError(nameof(ApiUrl), $"{nameof(ApiUrl)} is not specified.'");
            }
            if (string.IsNullOrEmpty(BranchName)) {
                throw new MissingConfigurationError(nameof(BranchName), $"{nameof(BranchName)} is not specified.'");
            }
            if (string.IsNullOrEmpty(Project)) {
                throw new MissingConfigurationError(nameof(Project), $"{nameof(Project)} is not specified.'");
            }
            if (string.IsNullOrEmpty(ApiKey)) {
                throw new MissingConfigurationError(nameof(ApiKey), $"{nameof(ApiKey)} is not specified.'");
            }
        }

        public static Config GetDefault()
        {
            return GetDefault(null);
        }

        public static Config GetDefault(string path)
        {
            var default_path = Path.Join(determine_config_path(), DefaultPath);
            Config cfg;

            if (path != null) {
                cfg = FromFile(path);
            } else if (File.Exists(default_path)) {
                cfg = FromFile(default_path);
            } else {
                cfg = new Config();
            }

            cfg.ApplyEnvironment();

            try
            {
                cfg.CheckComplete();
            }
            catch (MissingConfigurationError ex)
            {
                throw new MissingConfigurationError(
                    ex.FieldName,
                    $"Incomplete configuration, {ex.FieldName} was not specified. "
                    + $"Please provide via the constructor, a config file '{DefaultPath}', or environment variables.");
            }

            return cfg;
        }

        public static Config FromFile(string path)
        {
            using var file = File.OpenRead(path);
            try
            {
                return JsonSerializer.Deserialize<Config>(file, jsonOptions);
            }
            catch (JsonException ex)
            {
                throw new VisualRegressionTrackerError($"Cannot read config file '{path}': {ex.Message}", ex);
            }
        }

        public void ApplyEnvironment()
        {
            maybe_apply_env("VRT_APIURL", v => ApiUrl = v);
            maybe_apply_env("VRT_CIBUILDID", v => CiBuildId = v);
            maybe_apply_env("VRT_BRANCHNAME", v => BranchName = v);
            maybe_apply_env("VRT_PROJECT", v => Project = v);
            maybe_apply_env("VRT_APIKEY", v => ApiKey = v);
            maybe_apply_env("VRT_ENABLESOFTASSERT", v => EnableSoftAssert = IsTrue(v));
        }

        private static void maybe_apply_env(string name, Action<string> action)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) {
                action(value);
            }
        }

        private sealed class StringOrNumberConverter : JsonConverter<string>
        {
            public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Number
                    ? Encoding.UTF8.GetString(reader.ValueSpan)
                    : reader.GetString();
            }

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value);
            }
        }

        private sealed class BooleanLikeConverter : JsonConverter<bool>
        {
            public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return reader.TokenType switch
                {
                    JsonTokenType.True => true,
                    JsonTokenType.False => false,
                    JsonTokenType.Number => reader.GetDouble() != 0,
                    _ => IsTrue(reader.GetString())
                };
            }

            public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            {
                writer.WriteBooleanValue(value);
            }
        }

        private static bool IsTrue(string value)
        {
            return new[] {"true", "1"}.Contains(value.ToLowerInvariant());
        }

        private static string determine_config_path()
        {
            return Directory.GetCurrentDirectory();
        }
    }
}