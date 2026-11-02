using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace VisualRegressionTracker.Tests
{
    public class VisualRegressionTrackerTests
    {
        const string RunUrl = "http://frontend/project-1?buildId=build-1&testId=run-1";
        const string BuildJson = @"{
            ""id"":""build-1"",
            ""ciBuildId"":""build id"",
            ""number"":7,
            ""branchName"":""branch name"",
            ""status"":""new"",
            ""projectId"":""project-1"",
            ""updatedAt"":""2026-11-02T10:00:00.000Z"",
            ""createdAt"":""2026-11-02T10:00:00.000Z"",
            ""userId"":null,
            ""passedCount"":0,
            ""unresolvedCount"":0,
            ""failedCount"":0,
            ""isRunning"":true,
            ""merge"":false
        }";
        const string ErrorJson = @"{{""statusCode"":{0},""path"":""/{1}"",""name"":""Error"",""message"":""{2}""}}";

        readonly FakeHandler handler = new FakeHandler();
        readonly Config config;
        readonly VisualRegressionTracker vrt;

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
            vrt = new VisualRegressionTracker(config, new HttpClient(handler));
        }

        static string TestRunJson(string status, string diffName = "diff.png", string baselineName = "baseline.png")
        {
            return $@"{{
                ""id"":""run-1"",
                ""buildId"":""build-1"",
                ""imageName"":""image.png"",
                ""imageThumbnailName"":null,
                ""diffThumbnailName"":null,
                ""diffName"":{(diffName == null ? "null" : $@"""{diffName}""")},
                ""diffPercent"":1.5,
                ""diffTollerancePercent"":0,
                ""status"":""{status}"",
                ""testVariationId"":""variation-1"",
                ""name"":""image name"",
                ""baselineName"":{(baselineName == null ? "null" : $@"""{baselineName}""")},
                ""os"":null,
                ""browser"":null,
                ""viewport"":null,
                ""device"":null,
                ""customTags"":"""",
                ""ignoreAreas"":""[]"",
                ""tempIgnoreAreas"":""[]"",
                ""comment"":null,
                ""branchName"":""branch name"",
                ""baselineBranchName"":""master"",
                ""merge"":false,
                ""vlmDescription"":null,
                ""pixelMisMatchCount"":10,
                ""url"":""{RunUrl}""
            }}";
        }

        async Task StartBuild()
        {
            handler.Respond(HttpStatusCode.Created, BuildJson);
            await vrt.Start();
        }

        async Task<TestRunResult> TrackWithStatus(string status)
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson(status));
            return await vrt.Track("image name", "aW1hZ2U=");
        }

        void AssertMultipartImage(byte[] expected)
        {
            var request = handler.LastRequest;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:4200/test-runs/multipart", request.Url);
            Assert.Equal("api key", request.ApiKey);
            Assert.Equal("project", request.Project);
            var image = request.Form["image"];
            Assert.Equal(expected, image.Bytes);
            Assert.Equal("image.png", image.FileName);
            Assert.Equal("image/png", image.ContentType);
        }

        static string Value(RecordedRequest request, string name)
        {
            return request.Form.TryGetValue(name, out var part) ? part.Value : null;
        }

        [Fact]
        public void Defaults()
        {
            Assert.False(vrt.IsStarted);
            Assert.Null(vrt.BuildId);
            Assert.Null(vrt.ProjectId);
        }

        [Fact]
        public async Task Start_CreatesBuild()
        {
            await StartBuild();

            Assert.True(vrt.IsStarted);
            Assert.Equal("build-1", vrt.BuildId);
            Assert.Equal("project-1", vrt.ProjectId);
            var request = handler.LastRequest;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:4200/builds", request.Url);
            Assert.Equal("api key", request.ApiKey);
            Assert.Equal("project", request.Project);
            JsonAssert.Equal(
                @"{""branchName"":""branch name"",""project"":""project"",""ciBuildId"":""build id""}",
                request.Body);
        }

        [Fact]
        public async Task Start_WithCancellationToken()
        {
            handler.Respond(HttpStatusCode.Created, BuildJson);

            await vrt.Start(TestContext.Current.CancellationToken);

            Assert.Equal("build-1", vrt.BuildId);
        }

        [Fact]
        public async Task Start_OmitsCiBuildIdWhenNotConfigured()
        {
            config.CiBuildId = null;

            await StartBuild();

            JsonAssert.Equal(@"{""branchName"":""branch name"",""project"":""project""}", handler.LastRequest.Body);
        }

        [Fact]
        public async Task Start_WithTrailingSlashInApiUrl()
        {
            config.ApiUrl = "http://localhost:4200/";
            var tracker = new VisualRegressionTracker(config, new HttpClient(handler));
            handler.Respond(HttpStatusCode.Created, BuildJson);

            await tracker.Start();

            Assert.Equal("http://localhost:4200/builds", handler.LastRequest.Url);
        }

        [Fact]
        public async Task Start_WrapsNetworkErrors()
        {
            handler.Throw(new HttpRequestException("connection refused"));

            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => vrt.Start());

            Assert.Contains("connection refused", ex.Message);
        }

        [Fact]
        public async Task Start_ReportsServerMessage()
        {
            handler.Respond(HttpStatusCode.NotFound, string.Format(ErrorJson, 404, "builds", "Project not found: project"));

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Start());

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            Assert.Equal("Project not found: project", ex.ServerMessage);
            Assert.Equal("POST /builds failed with 404: Project not found: project", ex.Message);
        }

        [Fact]
        public async Task Stop_SendsIsRunningFalse()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.OK, BuildJson);

            await vrt.Stop();

            Assert.False(vrt.IsStarted);
            Assert.Null(vrt.BuildId);
            Assert.Null(vrt.ProjectId);
            var request = handler.LastRequest;
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("http://localhost:4200/builds/build-1", request.Url);
            Assert.Equal("api key", request.ApiKey);
            Assert.Equal("project", request.Project);
            JsonAssert.Equal(@"{""isRunning"":false}", request.Body);
        }

        [Fact]
        public async Task Stop_WithCancellationToken()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.OK, BuildJson);

            await vrt.Stop(TestContext.Current.CancellationToken);

            Assert.False(vrt.IsStarted);
        }

        [Fact]
        public async Task Start_DisposeStopsBuild()
        {
            handler.Respond(HttpStatusCode.Created, BuildJson);
            handler.Respond(HttpStatusCode.OK, BuildJson);

            await using (await vrt.Start())
            {
                Assert.True(vrt.IsStarted);
            }

            Assert.False(vrt.IsStarted);
            Assert.Equal(HttpMethod.Patch, handler.LastRequest.Method);
        }

        [Fact]
        public async Task Stop_ThrowsIfNotStarted()
        {
            await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => await vrt.Stop());
        }

        [Fact]
        public async Task Stop_WrapsNetworkErrors()
        {
            await StartBuild();
            handler.Throw(new HttpRequestException("connection reset"));

            await Assert.ThrowsAsync<VisualRegressionTrackerError>(async () => await vrt.Stop());
            Assert.True(vrt.IsStarted);
        }

        [Fact]
        public async Task Stop_ReportsServerMessage()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Forbidden, string.Format(ErrorJson, 403, "builds/build-1", "You do not have permission to perform this operation."));

            var ex = await Assert.ThrowsAsync<ServerError>(async () => await vrt.Stop());

            Assert.Contains("You do not have permission to perform this operation.", ex.Message);
        }

        [Fact]
        public async Task Track_SendsAllFields()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track(
                "image name",
                "aW1hZ2U=",
                TestContext.Current.CancellationToken,
                os: "os",
                browser: "browser",
                viewport: "viewport",
                device: "device",
                customTags: "customTags",
                diffTollerancePercent: 15,
                comment: "testcomment",
                ignoreAreas: new[] { new IgnoreAreaDto { X = 1, Y = 2, Width = 3, Height = 4 } });

            var request = handler.LastRequest;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://localhost:4200/test-runs", request.Url);
            Assert.Equal("api key", request.ApiKey);
            Assert.Equal("project", request.Project);
            JsonAssert.Equal(
                @"{
                    ""name"":""image name"",
                    ""imageBase64"":""aW1hZ2U="",
                    ""buildId"":""build-1"",
                    ""projectId"":""project-1"",
                    ""branchName"":""branch name"",
                    ""os"":""os"",
                    ""browser"":""browser"",
                    ""viewport"":""viewport"",
                    ""device"":""device"",
                    ""customTags"":""customTags"",
                    ""diffTollerancePercent"":15,
                    ""comment"":""testcomment"",
                    ""ignoreAreas"":[{""x"":1,""y"":2,""width"":3,""height"":4}]
                }",
                request.Body);
        }

        [Fact]
        public async Task Track_SendsOnlyGivenFields()
        {
            await TrackWithStatus("ok");

            JsonAssert.Equal(
                @"{
                    ""name"":""image name"",
                    ""imageBase64"":""aW1hZ2U="",
                    ""buildId"":""build-1"",
                    ""projectId"":""project-1"",
                    ""branchName"":""branch name""
                }",
                handler.LastRequest.Body);
        }

        [Fact]
        public async Task Track_BuildsImageUrlsFromApiUrl()
        {
            var result = await TrackWithStatus("ok");

            Assert.Equal(RunUrl, result.Url);
            Assert.Equal("http://localhost:4200/images/image.png", result.ImageUrl);
            Assert.Equal("http://localhost:4200/images/diff.png", result.DiffUrl);
            Assert.Equal("http://localhost:4200/images/baseline.png", result.BaselineUrl);
        }

        [Fact]
        public async Task Track_LeavesDiffAndBaselineUrlsEmptyWithoutImages()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok", diffName: null, baselineName: null));

            var result = await vrt.Track("image name", "aW1hZ2U=");

            Assert.Equal("http://localhost:4200/images/image.png", result.ImageUrl);
            Assert.Null(result.DiffUrl);
            Assert.Null(result.BaselineUrl);
        }

        [Fact]
        public async Task Track_ThrowsIfNotStarted()
        {
            await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => vrt.Track("name", "aW1hZ2U="));
        }

        [Theory]
        [InlineData("new", "No baseline: " + RunUrl)]
        [InlineData("unresolved", "Difference found: " + RunUrl)]
        public async Task Track_ThrowsOnNewAndUnresolved(string status, string expectedMessage)
        {
            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => TrackWithStatus(status));

            Assert.Equal(expectedMessage, ex.Message);
        }

        [Theory]
        [InlineData("ok", TestRunStatus.Ok)]
        [InlineData("approved", TestRunStatus.Approved)]
        [InlineData("autoApproved", TestRunStatus.AutoApproved)]
        [InlineData("failed", TestRunStatus.Failed)]
        public async Task Track_ReturnsOtherStatuses(string status, TestRunStatus expected)
        {
            var result = await TrackWithStatus(status);

            Assert.Equal(expected, result.Status);
        }

        [Fact]
        public async Task Track_ToleratesUnknownStatusWithWarning()
        {
            TestRunResult result = null;
            var log = await CaptureStderr(async () => result = await TrackWithStatus("somethingNew"));

            Assert.Equal(TestRunStatus.Unknown, result.Status);
            Assert.Contains("somethingNew", log);
            Assert.Contains(RunUrl, log);
        }

        [Theory]
        [InlineData("new", TestRunStatus.New, "No baseline: " + RunUrl)]
        [InlineData("unresolved", TestRunStatus.Unresolved, "Difference found: " + RunUrl)]
        public async Task Track_SoftAssertLogsInsteadOfThrowing(string status, TestRunStatus expected, string expectedLog)
        {
            config.EnableSoftAssert = true;
            TestRunResult result = null;

            var log = await CaptureStderr(async () => result = await TrackWithStatus(status));

            Assert.Equal(expected, result.Status);
            Assert.Contains(expectedLog, log);
        }

        [Fact]
        public async Task Track_PayloadTooLargeHasHint()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.RequestEntityTooLarge, string.Format(ErrorJson, 413, "test-runs", "request entity too large"));

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, ex.StatusCode);
            Assert.Equal("request entity too large", ex.ServerMessage);
            Assert.Contains("BODY_PARSER_JSON_LIMIT", ex.Message);
        }

        [Fact]
        public async Task Track_ImageTooLargeHasHint()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.UnprocessableEntity, string.Format(ErrorJson, 422, "test-runs", "Image is 20000x20000 (400000000 pixels), above MAX_IMAGE_PIXELS=100000000"));

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
            Assert.Contains("above MAX_IMAGE_PIXELS=100000000", ex.Message);
            Assert.Contains("smaller", ex.Message);
        }

        [Fact]
        public async Task Track_ReportsNonJsonErrorBody()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.BadGateway, "<html>Bad Gateway</html>", "text/html");

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
            Assert.Equal("<html>Bad Gateway</html>", ex.ServerMessage);
        }

        [Fact]
        public async Task Track_EmptyErrorBodyEndsMessageAtStatus()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.BadGateway, "", "text/plain");

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Equal("POST /test-runs failed with 502", ex.Message);
        }

        [Fact]
        public async Task Track_WrapsTimeout()
        {
            await StartBuild();
            handler.Throw(new TaskCanceledException("timed out", new TimeoutException()));

            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Contains("POST /test-runs", ex.Message);
            Assert.IsType<TaskCanceledException>(ex.InnerException);
        }

        [Theory]
        [InlineData(100_000)]
        [InlineData(-1)]
        public async Task Start_ReportsConnectTimeout(int timeoutMilliseconds)
        {
            using var tracker = new VisualRegressionTracker(
                config, new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds) });
            handler.Throw(new TaskCanceledException(
                "The operation was canceled.",
                new TimeoutException("A connection could not be established within the configured ConnectTimeout.")));

            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => tracker.Start());

            Assert.Equal(
                "POST /builds could not connect to http://localhost:4200: A connection could not be established within the configured ConnectTimeout.",
                ex.Message);
        }

        [Fact]
        public async Task Start_ReportsResponseTimeout()
        {
            using var tracker = new VisualRegressionTracker(config, new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(50) });

            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => tracker.Start());

            Assert.Equal("POST /builds timed out after 00:00:00.0500000", ex.Message);
        }

        [Fact]
        public async Task Track_PassesCallerCancellationThrough()
        {
            await StartBuild();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            handler.Throw(new TaskCanceledException("cancelled", null, cancelled.Token));

            await Assert.ThrowsAsync<TaskCanceledException>(() => vrt.Track("image name", "aW1hZ2U=", cancelled.Token));
        }

        [Fact]
        public async Task Start_ExplainsNonJsonSuccessResponse()
        {
            handler.Respond(HttpStatusCode.OK, "<html>Visual Regression Tracker</html>", "text/html");

            var ex = await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => vrt.Start());

            Assert.Contains("POST /builds", ex.Message);
            Assert.Contains("ApiUrl", ex.Message);
        }

        [Fact]
        public async Task Track_WrapsNetworkErrors()
        {
            await StartBuild();
            handler.Throw(new HttpRequestException("connection reset"));

            await Assert.ThrowsAsync<VisualRegressionTrackerError>(() => vrt.Track("image name", "aW1hZ2U="));
        }

        [Fact]
        public async Task Track_Stream()
        {
            var image = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", image);

            AssertMultipartImage(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Assert.True(image.CanRead);
        }

        [Fact]
        public async Task Track_SendsBase64WithoutEscaping()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", "+/+/");

            Assert.Contains(@"""imageBase64"":""+/+/""", handler.LastRequest.Body);
        }

        [Fact]
        public async Task Track_StreamFromCurrentPosition()
        {
            var image = new MemoryStream(new byte[] { 0xff, 0xff, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }) { Position = 2 };
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", image);

            AssertMultipartImage(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        }

        [Fact]
        public async Task Track_NonSeekableStream()
        {
            var image = new NonSeekableStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", image);

            AssertMultipartImage(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        }

        [Fact]
        public async Task Track_SendsSourceBranchAndMerge()
        {
            config.SourceBranch = "release";
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", "aW1hZ2U=", merge: true);

            JsonAssert.Equal(
                @"{
                    ""name"":""image name"",
                    ""imageBase64"":""aW1hZ2U="",
                    ""buildId"":""build-1"",
                    ""projectId"":""project-1"",
                    ""branchName"":""branch name"",
                    ""sourceBranch"":""release"",
                    ""merge"":true
                }",
                handler.LastRequest.Body);
        }

        [Fact]
        public async Task Track_SendsMergeFalse()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", "aW1hZ2U=", merge: false);

            Assert.Contains(@"""merge"":false", handler.LastRequest.Body);
        }

        [Fact]
        public async Task Track_MultipartSendsAllFields()
        {
            config.SourceBranch = "release";
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));
            try
            {
                await vrt.Track(
                    "image name",
                    new byte[] { 1, 2, 3 },
                    TestContext.Current.CancellationToken,
                    os: "os",
                    browser: "browser",
                    viewport: "viewport",
                    device: "device",
                    customTags: "customTags",
                    diffTollerancePercent: 1.5,
                    comment: "testcomment",
                    ignoreAreas: new[] { new IgnoreAreaDto { X = 1, Y = 2, Width = 3, Height = 4 } },
                    merge: true);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            AssertMultipartImage(new byte[] { 1, 2, 3 });
            var fields = handler.LastRequest.Form.Where(part => part.Key != "image").ToDictionary(part => part.Key, part => part.Value.Value);
            JsonAssert.Equal(@"[{""x"":1,""y"":2,""width"":3,""height"":4}]", fields["ignoreAreas"]);
            fields.Remove("ignoreAreas");
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["name"] = "image name",
                    ["buildId"] = "build-1",
                    ["projectId"] = "project-1",
                    ["branchName"] = "branch name",
                    ["sourceBranch"] = "release",
                    ["os"] = "os",
                    ["browser"] = "browser",
                    ["viewport"] = "viewport",
                    ["device"] = "device",
                    ["customTags"] = "customTags",
                    ["diffTollerancePercent"] = "1.5",
                    ["comment"] = "testcomment",
                    ["merge"] = "true",
                },
                fields);
        }

        [Fact]
        public async Task Track_MultipartSendsOnlyGivenFields()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", new byte[] { 1, 2, 3 });

            Assert.Equal(
                new[] { "branchName", "buildId", "image", "name", "projectId" },
                new SortedSet<string>(handler.LastRequest.Form.Keys, StringComparer.Ordinal));
        }

        [Fact]
        public async Task Track_File()
        {
            var path = Path.GetTempFileName();
            File.WriteAllBytes(path, new byte[] { 4, 5, 6 });
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));
            try
            {
                await vrt.Track("image name", new FileInfo(path), merge: false);
            }
            finally
            {
                File.Delete(path);
            }

            AssertMultipartImage(new byte[] { 4, 5, 6 });
            Assert.Equal("false", Value(handler.LastRequest, "merge"));
        }

        [Fact]
        public async Task Track_MultipartReportsFileTooLarge()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.RequestEntityTooLarge, string.Format(ErrorJson, 413, "test-runs/multipart", "File too large"));

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", new byte[] { 1 }));

            Assert.Equal("File too large", ex.ServerMessage);
            Assert.Contains("MULTIPART_FILE_SIZE_LIMIT", ex.Message);
        }

        [Fact]
        public async Task Track_RetriesServiceUnavailableAfterRetryAfter()
        {
            var delays = RecordDelays();
            await StartBuild();
            RespondBusy("5");
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            var result = await vrt.Track("image name", new byte[] { 1, 2, 3 });

            Assert.Equal(TestRunStatus.Ok, result.Status);
            Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, delays);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, handler.Requests[1].Form["image"].Bytes);
            AssertMultipartImage(new byte[] { 1, 2, 3 });
        }

        [Fact]
        public async Task Start_RetriesServiceUnavailableAfterRetryAfter()
        {
            var delays = RecordDelays();
            RespondBusy("1", "builds");
            handler.Respond(HttpStatusCode.Created, BuildJson);

            await vrt.Start();

            Assert.Equal("build-1", vrt.BuildId);
            Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, delays);
            JsonAssert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        }

        [Fact]
        public async Task Track_GivesUpAfterThreeRetries()
        {
            var delays = RecordDelays();
            await StartBuild();
            for (var i = 0; i < 4; i++)
            {
                RespondBusy("5");
            }

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.Equal("Image diff queue is full (100 jobs)", ex.ServerMessage);
            Assert.Equal(3, delays.Count);
            Assert.Equal(5, handler.Requests.Count);
        }

        [Fact]
        public async Task Track_DoesNotRetryServiceUnavailableWithoutRetryAfter()
        {
            var delays = RecordDelays();
            await StartBuild();
            RespondBusy(null);

            var ex = await Assert.ThrowsAsync<ServerError>(() => vrt.Track("image name", "aW1hZ2U="));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.Empty(delays);
        }

        [Fact]
        public async Task Track_WaitsAtMostAMinuteForRetryAfter()
        {
            var delays = RecordDelays();
            await StartBuild();
            RespondBusy("3600");
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", "aW1hZ2U=");

            Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, delays);
        }

        [Fact]
        public async Task Track_ReadsRetryAfterDate()
        {
            var delays = RecordDelays();
            await StartBuild();
            RespondBusy(DateTimeOffset.UtcNow.AddSeconds(-30).ToString("r"));
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", "aW1hZ2U=");

            Assert.Equal(new[] { TimeSpan.Zero }, delays);
        }

        void RespondBusy(string retryAfter, string path = "test-runs")
        {
            handler.Respond(
                HttpStatusCode.ServiceUnavailable,
                string.Format(ErrorJson, 503, path, "Image diff queue is full (100 jobs)"),
                retryAfter: retryAfter);
        }

        static async Task<string> CaptureStderr(Func<Task> action)
        {
            var log = new StringWriter();
            var stderr = Console.Error;
            Console.SetError(log);
            try
            {
                await action();
            }
            finally
            {
                Console.SetError(stderr);
            }
            return log.ToString();
        }

        List<TimeSpan> RecordDelays()
        {
            var delays = new List<TimeSpan>();
            vrt.Client.Delay = (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            };
            return delays;
        }

        [Fact]
        public void OwnHttpClient_WaitsForSlowComparisons()
        {
            using var tracker = new VisualRegressionTracker(config);

            Assert.Equal(TimeSpan.FromSeconds(360), tracker.OwnHttpClient.Timeout);
        }

        [Theory]
        [InlineData(30, 30_000)]
        [InlineData(0, -1)]
        public void OwnHttpClient_UsesConfiguredTimeoutWithZeroAsNoLimit(int seconds, int expectedMilliseconds)
        {
            config.HttpTimeoutInSeconds = seconds;
            using var tracker = new VisualRegressionTracker(config);

            Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), tracker.OwnHttpClient.Timeout);
        }

        [Fact]
        public void OwnHttpClient_ConnectTimeoutIsTenSeconds()
        {
            using var httpHandler = VisualRegressionTracker.CreateHttpHandler();

            Assert.Equal(TimeSpan.FromSeconds(10), httpHandler.ConnectTimeout);
        }

        [Fact]
        public void InjectedHttpClient_IsNotOwned()
        {
            using var tracker = new VisualRegressionTracker(config, new HttpClient(handler));

            Assert.Null(tracker.OwnHttpClient);
        }

        [Fact]
        public async Task Dispose_DisposesOwnHttpClient()
        {
            var tracker = new VisualRegressionTracker(config);

            tracker.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => tracker.Start());
        }

        [Fact]
        public async Task Dispose_LeavesInjectedHttpClientOpen()
        {
            var httpClient = new HttpClient(handler);
            new VisualRegressionTracker(config, httpClient).Dispose();
            handler.Respond(HttpStatusCode.OK);

            using var response = await httpClient.GetAsync("http://localhost:4200/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Track_Array()
        {
            await StartBuild();
            handler.Respond(HttpStatusCode.Created, TestRunJson("ok"));

            await vrt.Track("image name", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            AssertMultipartImage(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        }
    }
}
