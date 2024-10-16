using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq; 
using Xunit;

namespace VisualRegressionTracker.Tests
{
    public class VisualRegressionTrackerTests
    {
        public VisualRegressionTrackerTests() 
        {
            config = new Config
            {
                CiBuildId = "build id",
                BranchName = "branch name",
                Project = "project",
                ApiUrl = "http://localhost:4200",
                ApiKey = "api key"
            };
            vrt = new VisualRegressionTracker(config, new HttpClient(mock.Object));
        }

        readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        readonly Mock<HttpMessageHandler> mock = new Mock<HttpMessageHandler>();
        readonly Config config;
        readonly VisualRegressionTracker vrt;

        public static IEnumerable<object[]> Exceptions()
        {
            yield return new object[] {new ApiException("eeek", 1, "", null, null)};
            yield return new object[] {new ApiException<string>("eeek", 1, "", null, null, null)};
            yield return new object[] {new HttpRequestException("eek")};
        }

        [Fact]
        public void Defaults() 
        {
            Assert.False(vrt.IsStarted);
            Assert.Null(vrt.BuildId);
            Assert.Null(vrt.ProjectId);
        }

        [Fact]
        public async Task Start()
        {
            var buildDto = Builder<BuildDto>.CreateNew().Build();
            
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/builds",
                new CreateBuildDto
                {
                    BranchName = config.BranchName,
                    Project = config.Project,
                    CiBuildId = config.CiBuildId,
                },
                HttpStatusCode.Created,
                buildDto);

            await vrt.Start();

            Assert.True(vrt.IsStarted);
            Assert.Equal(buildDto.ProjectId, vrt.ProjectId);
            Assert.Equal(buildDto.Id, vrt.BuildId);

            mock.VerifyRequest(1, req => {
                req.Headers.TryGetValues("apiKey", out var apiKey);
                Assert.Equal(new[] {config.ApiKey}, apiKey);
                req.Headers.TryGetValues("project", out var project);
                Assert.Equal(new[] {config.Project}, project);
            });
        }

        [Fact]
        public async Task Start_CancellationToken()
        {
            var buildDto = Builder<BuildDto>.CreateNew().Build();
            
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/builds",
                new CreateBuildDto
                {
                    BranchName = config.BranchName,
                    Project = config.Project,
                    CiBuildId = config.CiBuildId,
                },
                HttpStatusCode.Created,
                buildDto);

            await vrt.Start(tokenSource.Token);

            Assert.True(vrt.IsStarted);
            Assert.Equal(buildDto.ProjectId, vrt.ProjectId);
            Assert.Equal(buildDto.Id, vrt.BuildId);

            mock.VerifyRequest(1, req => {
                req.Headers.TryGetValues("apiKey", out var apiKey);
                Assert.Equal(new[] {config.ApiKey}, apiKey);
                req.Headers.TryGetValues("project", out var project);
                Assert.Equal(new[] {config.Project}, project);
            });
        }

        [Theory]
        [MemberData(nameof(Exceptions))]
        public async Task Start_WrapsExceptions(Exception exception)
        {
            mock.SetupRequest(exception);

            await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => {
                await vrt.Start();
            });
        }

        [Fact]
        public async Task Start_DisposeCallsStop()
        {
            var buildDto = Builder<BuildDto>.CreateNew().Build();
            
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/builds",
                new CreateBuildDto
                {
                    BranchName = config.BranchName,
                    Project = config.Project,
                    CiBuildId = config.CiBuildId,
                },
                HttpStatusCode.Created,
                buildDto);

            await using (await vrt.Start())
            {
                Assert.True(vrt.IsStarted);
                Assert.Equal(buildDto.ProjectId, vrt.ProjectId);
                Assert.Equal(buildDto.Id, vrt.BuildId);

                mock.Reset();
                mock.SetupRequest(
                    HttpMethod.Patch,
                    "http://localhost:4200/builds/" + vrt.BuildId,
                    HttpStatusCode.OK);
            }
            
            Assert.False(vrt.IsStarted);
            Assert.Null(vrt.BuildId);
            Assert.Null(vrt.ProjectId);
        }

        [Fact]
        public async Task Stop()
        {
            await Start();
            
            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Patch,
                "http://localhost:4200/builds/" + vrt.BuildId,
                HttpStatusCode.OK);

            await vrt.Stop();

            Assert.False(vrt.IsStarted);
            Assert.Null(vrt.BuildId);
            Assert.Null(vrt.ProjectId);

            mock.VerifyRequest(1, req => {
                req.Headers.TryGetValues("apiKey", out var apiKey);
                Assert.Equal(new[] {config.ApiKey}, apiKey);
                req.Headers.TryGetValues("project", out var project);
                Assert.Equal(new[] {config.Project}, project);
            });
        }

        [Fact]
        public async Task Stop_CancellationToken()
        {
            await Start();
            
            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Patch,
                "http://localhost:4200/builds/" + vrt.BuildId,
                HttpStatusCode.OK);

            await vrt.Stop(tokenSource.Token);

            Assert.False(vrt.IsStarted);
            Assert.Null(vrt.BuildId);
            Assert.Null(vrt.ProjectId);

            mock.VerifyRequest(1, req => {
                req.Headers.TryGetValues("apiKey", out var apiKey);
                Assert.Equal(new[] {config.ApiKey}, apiKey);
                req.Headers.TryGetValues("project", out var project);
                Assert.Equal(new[] {config.Project}, project);
            });
        }

        [Fact]
        public async Task Stop_ThrowsIfNotStarted()
        {
            await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => {
                await vrt.Stop();
            });
        }

        [Theory]
        [MemberData(nameof(Exceptions))]
        public async Task Stop_WrapsExceptions(Exception exception)
        {
            await Start();

            mock.Reset();
            mock.SetupRequest(exception);

            await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => {
                await vrt.Stop();
            });
        }

        [Fact]
        public async Task Track()
        {
            await Start();

            var responseDto = Builder<TestRunResultDto>.CreateNew().Build();
            responseDto.Status = "ok";

            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/test-runs",
                new CreateTestRequestDto
                {
                    BranchName = config.BranchName,
                    ProjectId = vrt.ProjectId,
                    BuildId = vrt.BuildId,
                    Name = "image name",
                    ImageBase64 = "image base 64",
                    Os = "os",
                    Browser = "browser",
                    Viewport = "viewport",
                    Device = "device",
                    CustomTags = "customTags",
                    DiffTollerancePercent = 15,
                    Comment = "testcomment",
                    IgnoreAreas = new [] 
                    {
                        new IgnoreAreaDto{X=1, Y=2, Width=3, Height=4}
                    }
                },
                HttpStatusCode.Created,
                responseDto
            );

            var result = await vrt.Track(
                "image name",
                "image base 64",
                tokenSource.Token,
                os: "os",
                browser: "browser",
                viewport: "viewport",
                device: "device",
                customTags: "customTags",
                diffTollerancePercent: 15,
                comment: "testcomment",
                ignoreAreas: new[] 
                { 
                    new IgnoreAreaDto{X=1, Y=2, Width=3, Height=4}
                }
            );
            
            Assert.Equal(TestRunStatus.Ok, result.Status);
            Assert.Equal(responseDto.Url, result.Url);
            Assert.Equal(responseDto.Url + "/" + responseDto.ImageName, result.ImageUrl);
            Assert.Equal(responseDto.Url + "/" + responseDto.DiffName, result.DiffUrl);
            Assert.Equal(responseDto.Url + "/" + responseDto.BaselineName, result.BaselineUrl);
        }

        [Fact]
        public async Task Track_MinimalFields()
        {
            await Start();

            var responseDto = Builder<TestRunResultDto>.CreateNew().Build();
            responseDto.Status = "ok";
            responseDto.BaselineName = "";
            responseDto.DiffName = "";

            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/test-runs",
                new CreateTestRequestDto
                {
                    BranchName = config.BranchName,
                    ProjectId = vrt.ProjectId,
                    BuildId = vrt.BuildId,
                    Name = "image name",
                    ImageBase64 = "image base 64",
                },
                HttpStatusCode.Created,
                responseDto
            );

            var result = await vrt.Track(
                "image name",
                "image base 64"
            );

            Assert.Equal(TestRunStatus.Ok, result.Status);
            Assert.Equal(responseDto.Url, result.Url);
            Assert.Equal(responseDto.Url + "/" + responseDto.ImageName, result.ImageUrl);
            Assert.Null(result.DiffUrl);
            Assert.Null(result.BaselineUrl);
        }

        [Fact]
        public async Task Track_ThrowsIfNotStarted()
        {
            await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => {
                await vrt.Track("name", "image base 64");
            });
        }

        [Theory]
        [InlineData(false, "approved", "Unexpected status")]
        [InlineData(false, "failed", "Unexpected status")]
        [InlineData(false, "new", "No baseline: Url1")]
        [InlineData(false, "unresolved", "Difference found: Url1")]
        public async Task Track_ThrowsVisualRegressionTrackerError(bool enableSoftAssert, string status, string expectedMessage)
        {
            await Start();

            var responseDto = Builder<TestRunResultDto>.CreateNew().Build();
            responseDto.Status = status;

            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/test-runs",
                new CreateTestRequestDto
                {
                    BranchName = config.BranchName,
                    ProjectId = vrt.ProjectId,
                    BuildId = vrt.BuildId,
                    Name = "image name",
                    ImageBase64 = "image base 64",
                },
                HttpStatusCode.Created,
                responseDto
            );
            config.EnableSoftAssert = enableSoftAssert;

            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => 
            {
                 await vrt.Track("image name", "image base 64");
            });

            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData(true, "approved", TestRunStatus.Approved)]
        [InlineData(false, "autoApproved", TestRunStatus.AutoApproved)]
        [InlineData(true, "autoApproved", TestRunStatus.AutoApproved)]
        [InlineData(true, "failed", TestRunStatus.Failed)]
        [InlineData(true, "new", TestRunStatus.New)]
        [InlineData(false, "ok", TestRunStatus.Ok)]
        [InlineData(true, "ok", TestRunStatus.Ok)]
        [InlineData(true, "unresolved", TestRunStatus.Unresolved)]
        public async Task Track_ReturnStatus(bool enableSoftAssert, string status, TestRunStatus expectedStatus)
        {
            await Start();

            var responseDto = Builder<TestRunResultDto>.CreateNew().Build();
            responseDto.Status = status;

            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/test-runs",
                new CreateTestRequestDto
                {
                    BranchName = config.BranchName,
                    ProjectId = vrt.ProjectId,
                    BuildId = vrt.BuildId,
                    Name = "image name",
                    ImageBase64 = "image base 64",
                },
                HttpStatusCode.Created,
                responseDto
            );

            config.EnableSoftAssert = enableSoftAssert;
            var result = await vrt.Track("image name", "image base 64");

            Assert.Equal(expectedStatus, result.Status);
        }

        [Fact]
        public async Task Track_Stream()
        {
            var imageContent = new byte[] {0,1,2,3,4,5,6,7,8,9};
            var imageStream = new MemoryStream(imageContent);
            var expectedBase64 = Convert.ToBase64String(imageContent);

            await Start();

            var responseDto = Builder<TestRunResultDto>.CreateNew().Build();
            responseDto.Status = "ok";

            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/test-runs",
                new CreateTestRequestDto
                {
                    BranchName = config.BranchName,
                    ProjectId = vrt.ProjectId,
                    BuildId = vrt.BuildId,
                    Name = "image name",
                    ImageBase64 = expectedBase64,
                },
                HttpStatusCode.Created,
                responseDto
            );

            await vrt.Track("image name", imageStream);
        }

        [Fact]
        public async Task Track_Array()
        {
            var imageContent = new byte[] {0,1,2,3,4,5,6,7,8,9};
            var expectedBase64 = Convert.ToBase64String(imageContent);

            await Start();

            var responseDto = Builder<TestRunResultDto>.CreateNew().Build();
            responseDto.Status = "ok";

            mock.Reset();
            mock.SetupRequest(
                HttpMethod.Post,
                "http://localhost:4200/test-runs",
                new CreateTestRequestDto
                {
                    BranchName = config.BranchName,
                    ProjectId = vrt.ProjectId,
                    BuildId = vrt.BuildId,
                    Name = "image name",
                    ImageBase64 = expectedBase64,
                },
                HttpStatusCode.Created,
                responseDto
            );

            await vrt.Track("image name", imageContent);
        }
    }
}
