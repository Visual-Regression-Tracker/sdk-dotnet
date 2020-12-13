using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VisualRegressionTracker
{
    public class VisualRegressionTracker
    {
        private class Disposer : IAsyncDisposable 
        {
            private readonly VisualRegressionTracker tracker;
            private readonly CancellationToken? cancellationToken;

            public Disposer(VisualRegressionTracker tracker, CancellationToken? cancellationToken)
            {
                this.tracker = tracker;
                this.cancellationToken = cancellationToken;
            }

            public ValueTask DisposeAsync()
            {
                return tracker.Stop(cancellationToken);
            }
        }

        private readonly Config config;
        private readonly ApiClient client;
        private string buildId;
        private string projectId;

        public VisualRegressionTracker(Config config = null, HttpClient httpClient = null)
        {
            this.config = config ?? Config.GetDefault();
            this.config.CheckComplete();

            this.client = new ApiClient(
                this.config.ApiUrl,
                httpClient ?? new HttpClient()
            )
            {
                ApiKey = this.config.ApiKey
            };
        }

        public bool IsStarted => buildId != null && projectId != null;
        public string BuildId => buildId;
        public string ProjectId => projectId;

        public async Task<IAsyncDisposable> Start(CancellationToken? cancellationToken = null)
        {
            try
            {
                var dto = new CreateBuildDto
                {
                    Project = config.Project,
                    BranchName = config.BranchName,
                    CiBuildId = config.CiBuildId
                };
                var response = cancellationToken.HasValue
                    ? await client.BuildsController_createAsync(dto, cancellationToken.Value)
                    : await client.BuildsController_createAsync(dto);

                buildId = response.Id;
                projectId = response.ProjectId;
                return new Disposer(this, cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
            catch (HttpRequestException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
        }

        public async ValueTask Stop(CancellationToken? cancellationToken = null)
        {
            if (!IsStarted)
                throw new VisualRegressionTrackerError("Visual Regression Tracker has not been started");

            try
            {
                var response = cancellationToken.HasValue
                    ? await client.BuildsController_stopAsync(buildId, cancellationToken.Value)
                    : await client.BuildsController_stopAsync(buildId);
                buildId = null;
                projectId = null;
            }
            catch (ApiException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
            catch (HttpRequestException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
        }

        protected async Task<TestRunResult> SubmitTestRun(
            CreateTestRequestDto testRun, CancellationToken? cancellationToken)
        {
            if (!IsStarted)
                throw new VisualRegressionTrackerError("Visual Regression Tracker has not been started");

            var response = cancellationToken.HasValue
                ? await client.TestRunsController_postTestRunAsync(testRun, cancellationToken.Value)
                : await client.TestRunsController_postTestRunAsync(testRun);

            var status = response.Status switch
            {
                "new" => TestRunStatus.New,
                "ok" => TestRunStatus.Ok,
                "unresolved" => TestRunStatus.Unresolved,
                _ => throw new VisualRegressionTrackerError("Unexpected status")
            };

            var result = new TestRunResult
            {
                Status = status,
                Url = response.Url,
                ImageUrl = $"{response.Url}/{response.ImageName}",
                BaselineUrl = !string.IsNullOrEmpty(response.BaselineName)
                    ? $"{response.Url}/{response.BaselineName}"
                    : null,
                DiffUrl = !string.IsNullOrEmpty(response.DiffName)
                    ? $"{response.Url}/{response.DiffName}"
                    : null,
            };

            return result;
        }

        public async Task<TestRunResult> Track(
            string name,
            string imageBase64,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            double? diffTollerancePercent = default,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null)
        {
            var dto = new CreateTestRequestDto
            {
                BuildId = BuildId,
                ProjectId = ProjectId,
                BranchName = config.BranchName,
                Name = name,
                ImageBase64 = imageBase64,
                Os = os,
                Browser = browser,
                Viewport = viewport,
                Device = device,
                DiffTollerancePercent = diffTollerancePercent,
                IgnoreAreas = ignoreAreas == null ? null : new List<IgnoreAreaDto>(ignoreAreas)
            };

            var result = await SubmitTestRun(dto, cancellationToken);

            if (!config.EnableSoftAssert && result.Status != TestRunStatus.Ok)
            {
                throw new VisualRegressionTrackerError(result.Status switch
                {
                    TestRunStatus.New => $"No baseline: {result.Url}",
                    TestRunStatus.Unresolved => $"Difference found: {result.Url}",
                    _ => throw new VisualRegressionTrackerError("Unexpected status")
                });
            }

            return result;
        }
 
        public Task<TestRunResult> Track(
            string name,
            Stream image,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            double? diffTollerancePercent = default,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null)
        {
            using var base64Stream = new CryptoStream(image, new ToBase64Transform(), CryptoStreamMode.Read);
            using var reader = new StreamReader(base64Stream);
            var imageBase64 = reader.ReadToEnd();

            return Track(
                name,
                imageBase64,
                cancellationToken,
                os, browser, viewport, device, diffTollerancePercent, ignoreAreas
            );
        }

        public Task<TestRunResult> Track(
            string name,
            byte[] image,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            double? diffTollerancePercent = default,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null)
        {
            using var memoryStream = new MemoryStream(image);

            return Track(
                name,
                memoryStream,
                cancellationToken,
                os, browser, viewport, device, diffTollerancePercent, ignoreAreas
            );
        }
    }
}