using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.v3;
using System.Text.Json;

namespace VisualRegressionTracker.Tests
{
    public class ConfigTests
    {
        public static IEnumerable<object[]> Configs()
        {
            yield return new object[] {new Config
            {
                ApiUrl = "http://vrt:4200", CiBuildId = "ci 1", BranchName = "main",
                Project = "Default project", ApiKey = "key 1", EnableSoftAssert = true,
                SourceBranch = "release", HttpTimeoutInSeconds = 0
            }};
            yield return new object[] {new Config
            {
                ApiUrl = "https://vrt.example.com/api", CiBuildId = null, BranchName = "feature/x",
                Project = "b4c8e5f0-0000-4000-8000-000000000000", ApiKey = "key 2", EnableSoftAssert = false,
                SourceBranch = null, HttpTimeoutInSeconds = 360
            }};
        }

        [Theory]
        [MemberData(nameof(Configs))]
        public void JsonRoundTrip(Config expected)
        {
            var json = JsonSerializer.Serialize(expected);
            var actual = JsonSerializer.Deserialize<Config>(json);
            var json2 = JsonSerializer.Serialize(actual);

            Assert.Equal(json, json2);
            AssertConfigEqual(expected, actual);
        }

        [Theory]
        [MemberData(nameof(Configs))]
        public void FromFile(Config expected)
        {
            var path = "ConfigTests.FromFile.json";
            File.WriteAllText(path, JsonSerializer.Serialize(expected));
            var actual = Config.FromFile(path);

            AssertConfigEqual(expected, actual);
        }

        [Fact]
        [MoveCurrentDirectory]
        public void FromFile_AcceptsCommentsAndAnyCase()
        {
            File.WriteAllText("vrt.json", @"{
                // local server
                ""ApiUrl"": ""http://vrt:4200"",
                ""PROJECT"": ""Default project"",
                ""enableSoftAssert"": true,
            }");

            var actual = Config.FromFile("vrt.json");

            Assert.Equal("http://vrt:4200", actual.ApiUrl);
            Assert.Equal("Default project", actual.Project);
            Assert.True(actual.EnableSoftAssert);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("\"true\"", true)]
        [InlineData("\"True\"", true)]
        [InlineData("\"1\"", true)]
        [InlineData("1", true)]
        [InlineData("\"false\"", false)]
        [InlineData("0", false)]
        [MoveCurrentDirectory]
        public void FromFile_AcceptsBooleanLikeEnableSoftAssert(string json, bool expected)
        {
            File.WriteAllText(Config.DefaultPath, $@"{{""enableSoftAssert"": {json}}}");

            Assert.Equal(expected, Config.FromFile(Config.DefaultPath).EnableSoftAssert);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("{}")]
        [MoveCurrentDirectory]
        public void FromFile_ReportsInvalidEnableSoftAssert(string json)
        {
            File.WriteAllText(Config.DefaultPath, $@"{{""enableSoftAssert"": {json}}}");

            var ex = Assert.Throws<VisualRegressionTrackerError>(() => Config.FromFile(Config.DefaultPath));

            Assert.Contains(Config.DefaultPath, ex.Message);
        }

        [Fact]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_ReportsUnreadableConfigFile()
        {
            File.WriteAllText(Config.DefaultPath, "{'apiUrl': 'http://vrt:4200'}");

            var ex = Assert.Throws<VisualRegressionTrackerError>(() => Config.GetDefault());

            Assert.Contains(Config.DefaultPath, ex.Message);
            Assert.IsType<JsonException>(ex.InnerException, exactMatch: false);
        }

        [Fact]
        [MoveCurrentDirectory]
        public void FromFile_AcceptsNumericCiBuildId()
        {
            File.WriteAllText("vrt.json", @"{""ciBuildId"": 1234}");

            Assert.Equal("1234", Config.FromFile("vrt.json").CiBuildId);
        }

        [Theory]
        [MemberData(nameof(Configs))]
        [ClearEnvironment]
        public void ApplyEnvironment(Config expected)
        {   
            Environment.SetEnvironmentVariable("VRT_APIURL", expected.ApiUrl);
            Environment.SetEnvironmentVariable("VRT_CIBUILDID", expected.CiBuildId);
            Environment.SetEnvironmentVariable("VRT_PROJECT", expected.Project);
            Environment.SetEnvironmentVariable("VRT_BRANCHNAME", expected.BranchName);
            Environment.SetEnvironmentVariable("VRT_APIKEY", expected.ApiKey);
            Environment.SetEnvironmentVariable("VRT_ENABLESOFTASSERT", expected.EnableSoftAssert ? "true" : "false");
            Environment.SetEnvironmentVariable("VRT_SOURCEBRANCH", expected.SourceBranch);
            Environment.SetEnvironmentVariable("VRT_HTTPTIMEOUTINSECONDS", expected.HttpTimeoutInSeconds.ToString());

            var actual = new Config();
            actual.ApplyEnvironment();

            AssertConfigEqual(expected, actual);
        }

        static void AssertConfigEqual(Config expected, Config actual)
        {
            Assert.Equal(expected.ApiUrl, actual.ApiUrl);
            Assert.Equal(expected.CiBuildId, actual.CiBuildId);
            Assert.Equal(expected.Project, actual.Project);
            Assert.Equal(expected.BranchName, actual.BranchName);
            Assert.Equal(expected.ApiKey, actual.ApiKey);
            Assert.Equal(expected.EnableSoftAssert, actual.EnableSoftAssert);
            Assert.Equal(expected.SourceBranch, actual.SourceBranch);
            Assert.Equal(expected.HttpTimeoutInSeconds, actual.HttpTimeoutInSeconds);
        }

        [Fact]
        public void HttpTimeoutInSecondsDefaultsTo360()
        {
            Assert.Equal(360, new Config().HttpTimeoutInSeconds);
        }

        [Theory]
        [InlineData("30")]
        [InlineData("\"30\"")]
        [MoveCurrentDirectory]
        public void FromFile_AcceptsHttpTimeoutAsNumberOrString(string json)
        {
            File.WriteAllText(Config.DefaultPath, $@"{{""httpTimeoutInSeconds"": {json}}}");

            Assert.Equal(30, Config.FromFile(Config.DefaultPath).HttpTimeoutInSeconds);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("1.5")]
        [ClearEnvironment]
        public void ApplyEnvironment_RejectsInvalidHttpTimeout(string value)
        {
            Environment.SetEnvironmentVariable("VRT_HTTPTIMEOUTINSECONDS", value);

            var ex = Assert.Throws<VisualRegressionTrackerError>(() => new Config().ApplyEnvironment());

            Assert.Contains("VRT_HTTPTIMEOUTINSECONDS", ex.Message);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2_147_484)]
        public void CheckComplete_RejectsHttpTimeoutOutOfRange(int seconds)
        {
            var cfg = new Config { BranchName = "main", Project = "project", ApiKey = "key", HttpTimeoutInSeconds = seconds };

            var ex = Assert.Throws<VisualRegressionTrackerError>(() => cfg.CheckComplete());

            Assert.Contains("HttpTimeoutInSeconds", ex.Message);
        }

        [Fact]
        public void CheckComplete()
        {
            var cfg = new Config {ApiUrl = null};

            var ex = Assert.Throws<MissingConfigurationError>(() => cfg.CheckComplete());
            Assert.Equal("ApiUrl", ex.FieldName);
            cfg.ApiUrl = "1";

            ex = Assert.Throws<MissingConfigurationError>(() => cfg.CheckComplete());
            Assert.Equal("BranchName", ex.FieldName);
            cfg.BranchName = "2";

            ex = Assert.Throws<MissingConfigurationError>(() => cfg.CheckComplete());
            Assert.Equal("Project", ex.FieldName);
            cfg.Project = "3";

            ex = Assert.Throws<MissingConfigurationError>(() => cfg.CheckComplete());
            Assert.Equal("ApiKey", ex.FieldName);
        }

        [Fact]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_None()
        {
            var ex = Assert.Throws<MissingConfigurationError>(() => Config.GetDefault());

            Assert.Contains("specified. Please provide", ex.Message);
        }

        [Theory]
        [MemberData(nameof(Configs))]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_DefaultPath(Config expected)
        {
            File.WriteAllText(Config.DefaultPath, JsonSerializer.Serialize(expected));
            var actual = Config.GetDefault();

            AssertConfigEqual(expected, actual);
        }

        [Theory]
        [MemberData(nameof(Configs))]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_Path(Config expected)
        {
            File.WriteAllText("bob.json", JsonSerializer.Serialize(expected));
            var actual = Config.GetDefault("bob.json");

            AssertConfigEqual(expected, actual);
        }

        [Fact]
        [MoveCurrentDirectory]
        public void GetDefault_ThrowsOnPathNotExist()
        {
            Assert.Throws<FileNotFoundException>(() => Config.GetDefault("bob.json"));
        }

        [Theory]
        [MemberData(nameof(Configs))]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_Combined(Config fileConfig)
        {
            File.WriteAllText(Config.DefaultPath, JsonSerializer.Serialize(fileConfig));
            Environment.SetEnvironmentVariable("VRT_PROJECT", "overridden");

            var actual = Config.GetDefault();

            Assert.Equal(fileConfig.ApiUrl, actual.ApiUrl);
            Assert.Equal(fileConfig.CiBuildId, actual.CiBuildId);
            Assert.Equal("overridden", actual.Project);
            Assert.Equal(fileConfig.BranchName, actual.BranchName);
            Assert.Equal(fileConfig.ApiKey, actual.ApiKey);
            Assert.Equal(fileConfig.EnableSoftAssert, actual.EnableSoftAssert);
            Assert.Equal(fileConfig.SourceBranch, actual.SourceBranch);
            Assert.Equal(fileConfig.HttpTimeoutInSeconds, actual.HttpTimeoutInSeconds);
        }
    }

    public class MoveCurrentDirectory : BeforeAfterTestAttribute
    {
        private string oldCurrentDir;

        public override void Before(MethodInfo methodUnderTest, IXunitTest test)
        {
            oldCurrentDir = Directory.GetCurrentDirectory();
            var newDir = $"{methodUnderTest.DeclaringType.Name}.{methodUnderTest.Name}";
            
            if (Directory.Exists(newDir)) {
                Directory.Delete(newDir, true);
            }

            Directory.CreateDirectory(newDir);
            Directory.SetCurrentDirectory(newDir);
        }

        public override void After(MethodInfo methodUnderTest, IXunitTest test)
        {
            Directory.SetCurrentDirectory(oldCurrentDir);
        }
    }

    public class ClearEnvironment : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest, IXunitTest test)
        {
            Environment.SetEnvironmentVariable("VRT_APIURL", null);
            Environment.SetEnvironmentVariable("VRT_CIBUILDID", null);
            Environment.SetEnvironmentVariable("VRT_PROJECT", null);
            Environment.SetEnvironmentVariable("VRT_BRANCHNAME", null);
            Environment.SetEnvironmentVariable("VRT_APIKEY", null);
            Environment.SetEnvironmentVariable("VRT_ENABLESOFTASSERT", null);
            Environment.SetEnvironmentVariable("VRT_SOURCEBRANCH", null);
            Environment.SetEnvironmentVariable("VRT_HTTPTIMEOUTINSECONDS", null);
        }

        public override void After(MethodInfo methodUnderTest, IXunitTest test)
        {
            Before(methodUnderTest, test);
        }
    }
}