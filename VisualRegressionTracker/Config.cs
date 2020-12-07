using System;
using System.IO;
using Newtonsoft.Json;

namespace VisualRegressionTracker
{
    public class Config
    {
        private static readonly JsonSerializer serializer = new JsonSerializer();

        [JsonProperty("apiUrl")]
        public string ApiUrl { get; set; } = "http://localhost:4200";
        [JsonProperty("aciBuildIdpiUrl")]
        public string CiBuildId { get; set; }
        [JsonProperty("branchName")]
        public string BranchName { get; set; }
        [JsonProperty("project")]
        public string Project { get; set; }
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }
        [JsonProperty("enableSoftAssert")]
        public bool EnableSoftAssert { get; set; }

        public void check_config()
        {

        }

        public static Config get_default()
        {
            using (var file = File.OpenText(@".\vrt.json"))
            {
                var config = (Config)serializer.Deserialize(file, typeof(Config));
                return config;
            }
        }
    }
}