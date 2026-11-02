using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VisualRegressionTracker
{
    public class VisualRegressionTracker : IDisposable
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
        internal readonly HttpClient OwnHttpClient;
        private string buildId;
        private string projectId;

        public VisualRegressionTracker() : this(null, null)
        {
        }

        public VisualRegressionTracker(Config config) : this(config, null) 
        {
        }

        public VisualRegressionTracker(Config config, HttpClient httpClient)
        {
            this.config = config ?? Config.GetDefault();
            this.config.CheckComplete();

            // the server compares the images inside POST /test-runs, and a VLM check can take 300 s
            OwnHttpClient = httpClient == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(360) } : null;

            client = new ApiClient(
                httpClient ?? OwnHttpClient,
                this.config.ApiUrl,
                this.config.ApiKey,
                this.config.Project);
        }

        public void Dispose()
        {
            OwnHttpClient?.Dispose();
        }

        public bool IsStarted => buildId != null && projectId != null;
        public string BuildId => buildId;
        public string ProjectId => projectId;

        public Task<IAsyncDisposable> Start()
        {
            return Start(null);
        }

        public Task<IAsyncDisposable> Start(CancellationToken cancellationToken)
        {
            return Start((CancellationToken?) cancellationToken);
        }

        protected async Task<IAsyncDisposable> Start(CancellationToken? cancellationToken)
        {
            var build = await client.CreateBuild(
                new CreateBuildDto(config.BranchName, config.Project, config.CiBuildId),
                cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            buildId = build.Id;
            projectId = build.ProjectId;
            return new Disposer(this, cancellationToken);
        }

        public ValueTask Stop()
        {
            return Stop(null);
        }
         
        public ValueTask Stop(CancellationToken cancellationToken)
        {
            return Stop((CancellationToken?) cancellationToken);
        }

        protected async ValueTask Stop(CancellationToken? cancellationToken)
        {
            EnsureStarted();

            await client.StopBuild(buildId, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            buildId = null;
            projectId = null;
        }

        public async Task<TestRunResult> Track(
            string name,
            string imageBase64,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            string customTags = null,
            double? diffTollerancePercent = null,
            string comment = null,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null)
        {
            EnsureStarted();

            var response = await client.PostTestRun(
                new CreateTestRequestDto
                {
                    Name = name,
                    ImageBase64 = imageBase64,
                    BuildId = buildId,
                    ProjectId = projectId,
                    BranchName = config.BranchName,
                    Os = os,
                    Browser = browser,
                    Viewport = viewport,
                    Device = device,
                    CustomTags = customTags,
                    DiffTollerancePercent = diffTollerancePercent,
                    Comment = comment,
                    IgnoreAreas = ignoreAreas
                },
                cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            var result = new TestRunResult
            {
                Status = ParseStatus(response.Status),
                Url = response.Url,
                ImageUrl = client.ImageUrl(response.ImageName),
                DiffUrl = client.ImageUrl(response.DiffName),
                BaselineUrl = client.ImageUrl(response.BaselineName),
            };

            var failure = result.Status switch
            {
                TestRunStatus.New => $"No baseline: {result.Url}",
                TestRunStatus.Unresolved => $"Difference found: {result.Url}",
                _ => null
            };
            if (failure != null) {
                if (!config.EnableSoftAssert) {
                    throw new VisualRegressionTrackerError(failure);
                }
                Console.Error.WriteLine(failure);
            }

            return result;
        }

        public async Task<TestRunResult> Track(
            string name,
            Stream image,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            string customTags = null,
            double? diffTollerancePercent = null,
            string comment = null,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null)
        {
            using var buffer = new MemoryStream(image.CanSeek ? (int)(image.Length - image.Position) : 0);
            await image.CopyToAsync(buffer, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return await Track(
                name,
                Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length),
                cancellationToken,
                os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas
            ).ConfigureAwait(false);
        }

        public Task<TestRunResult> Track(
            string name,
            byte[] image,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            string customTags = null,
            double? diffTollerancePercent = null,
            string comment = null,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null)
        {
            return Track(
                name,
                Convert.ToBase64String(image),
                cancellationToken,
                os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas
            );
        }

        private void EnsureStarted()
        {
            if (!IsStarted) {
                throw new VisualRegressionTrackerError("Visual Regression Tracker has not been started");
            }
        }

        private static TestRunStatus ParseStatus(string status)
        {
            return status switch
            {
                "new" => TestRunStatus.New,
                "ok" => TestRunStatus.Ok,
                "unresolved" => TestRunStatus.Unresolved,
                "failed" => TestRunStatus.Failed,
                "approved" => TestRunStatus.Approved,
                "autoApproved" => TestRunStatus.AutoApproved,
                _ => TestRunStatus.Unknown
            };
        }
    }
}
