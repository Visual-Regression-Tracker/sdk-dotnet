using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FizzWare.NBuilder;
using Xunit;
using Xunit.Sdk;
using Newtonsoft.Json;

namespace VisualRegressionTracker.Tests
{
    public class ConfigTests
    {
        public static IEnumerable<object[]> Configs()
        {
            foreach (var c in Builder<Config>.CreateListOfSize(10).Build()) {
                yield return new[] {c};
            }
        }

        [Theory]
        [MemberData(nameof(Configs))]
        public void JsonRoundTrip(Config expected)
        {
            var json = JsonConvert.SerializeObject(expected);
            var actual = (Config)JsonConvert.DeserializeObject(json, typeof(Config));
            var json2 = JsonConvert.SerializeObject(actual);

            Assert.Equal(json, json2);
            Assert.Equal(expected.ApiUrl, actual.ApiUrl);
            Assert.Equal(expected.CiBuildId, actual.CiBuildId);
            Assert.Equal(expected.Project, actual.Project);
            Assert.Equal(expected.BranchName, actual.BranchName);
            Assert.Equal(expected.ApiKey, actual.ApiKey);
            Assert.Equal(expected.EnableSoftAssert, actual.EnableSoftAssert);
        }

        [Theory]
        [MemberData(nameof(Configs))]
        public void FromFile(Config expected)
        {
            var path = "ConfigTests.FromFile.json";
            File.WriteAllText(path, JsonConvert.SerializeObject(expected));
            var actual = Config.FromFile(path);

            Assert.Equal(expected.ApiUrl, actual.ApiUrl);
            Assert.Equal(expected.CiBuildId, actual.CiBuildId);
            Assert.Equal(expected.Project, actual.Project);
            Assert.Equal(expected.BranchName, actual.BranchName);
            Assert.Equal(expected.ApiKey, actual.ApiKey);
            Assert.Equal(expected.EnableSoftAssert, actual.EnableSoftAssert);
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

            var actual = new Config();
            actual.ApplyEnvironment();

            Assert.Equal(expected.ApiUrl, actual.ApiUrl);
            Assert.Equal(expected.CiBuildId, actual.CiBuildId);
            Assert.Equal(expected.Project, actual.Project);
            Assert.Equal(expected.BranchName, actual.BranchName);
            Assert.Equal(expected.ApiKey, actual.ApiKey);
            Assert.Equal(expected.EnableSoftAssert, actual.EnableSoftAssert);
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
            Assert.Throws<MissingConfigurationError>(() => Config.GetDefault());
        }

        [Theory]
        [MemberData(nameof(Configs))]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_DefaultPath(Config expected)
        {
            File.WriteAllText(Config.DefaultPath, JsonConvert.SerializeObject(expected));
            var actual = Config.GetDefault();

            Assert.Equal(expected.ApiUrl, actual.ApiUrl);
            Assert.Equal(expected.CiBuildId, actual.CiBuildId);
            Assert.Equal(expected.Project, actual.Project);
            Assert.Equal(expected.BranchName, actual.BranchName);
            Assert.Equal(expected.ApiKey, actual.ApiKey);
            Assert.Equal(expected.EnableSoftAssert, actual.EnableSoftAssert);
        }

        [Theory]
        [MemberData(nameof(Configs))]
        [MoveCurrentDirectory]
        [ClearEnvironment]
        public void GetDefault_Path(Config expected)
        {
            File.WriteAllText("bob.json", JsonConvert.SerializeObject(expected));
            var actual = Config.GetDefault("bob.json");

            Assert.Equal(expected.ApiUrl, actual.ApiUrl);
            Assert.Equal(expected.CiBuildId, actual.CiBuildId);
            Assert.Equal(expected.Project, actual.Project);
            Assert.Equal(expected.BranchName, actual.BranchName);
            Assert.Equal(expected.ApiKey, actual.ApiKey);
            Assert.Equal(expected.EnableSoftAssert, actual.EnableSoftAssert);
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
            File.WriteAllText(Config.DefaultPath, JsonConvert.SerializeObject(fileConfig));
            Environment.SetEnvironmentVariable("VRT_PROJECT", "overridden");

            var actual = Config.GetDefault();

            Assert.Equal(fileConfig.ApiUrl, actual.ApiUrl);
            Assert.Equal(fileConfig.CiBuildId, actual.CiBuildId);
            Assert.Equal("overridden", actual.Project);
            Assert.Equal(fileConfig.BranchName, actual.BranchName);
            Assert.Equal(fileConfig.ApiKey, actual.ApiKey);
            Assert.Equal(fileConfig.EnableSoftAssert, actual.EnableSoftAssert);
        }
    }

    public class MoveCurrentDirectory : BeforeAfterTestAttribute
    {
        private string oldCurrentDir;

        public override void Before(MethodInfo methodUnderTest)
        {
            oldCurrentDir = Directory.GetCurrentDirectory();
            var newDir = $"{methodUnderTest.DeclaringType.Name}.{methodUnderTest.Name}";
            
            if (Directory.Exists(newDir)) {
                Directory.Delete(newDir, true);
            }

            Directory.CreateDirectory(newDir);
            Directory.SetCurrentDirectory(newDir);
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Directory.SetCurrentDirectory(oldCurrentDir);
        }
    }

    public class ClearEnvironment : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest)
        {
            Environment.SetEnvironmentVariable("VRT_APIURL", null);
            Environment.SetEnvironmentVariable("VRT_CIBUILDID", null);
            Environment.SetEnvironmentVariable("VRT_PROJECT", null);
            Environment.SetEnvironmentVariable("VRT_BRANCHNAME", null);
            Environment.SetEnvironmentVariable("VRT_APIKEY", null);
            Environment.SetEnvironmentVariable("VRT_ENABLESOFTASSERT", null);
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Before(methodUnderTest);
        }
    }
}