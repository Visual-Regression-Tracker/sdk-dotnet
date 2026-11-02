using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        internal readonly HttpClient OwnHttpClient;
        internal ApiClient Client { get; }
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

            OwnHttpClient = httpClient == null
                ? new HttpClient(CreateHttpHandler())
                {
                    Timeout = this.config.HttpTimeoutInSeconds == 0
                        ? Timeout.InfiniteTimeSpan
                        : TimeSpan.FromSeconds(this.config.HttpTimeoutInSeconds)
                }
                : null;

            Client = new ApiClient(
                httpClient ?? OwnHttpClient,
                this.config.ApiUrl,
                this.config.ApiKey,
                this.config.Project);
        }

        internal static SocketsHttpHandler CreateHttpHandler()
        {
            return new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) };
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
            var build = await Client.CreateBuild(
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

            await Client.StopBuild(buildId, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

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
            IEnumerable<IgnoreAreaDto> ignoreAreas = null,
            bool? merge = null)
        {
            var testRun = TestRun(name, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge) with { ImageBase64 = imageBase64 };
            return Result(await Client.PostTestRun(testRun, cancellationToken ?? CancellationToken.None).ConfigureAwait(false));
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
            IEnumerable<IgnoreAreaDto> ignoreAreas = null,
            bool? merge = null)
        {
            using var buffer = new MemoryStream(image.CanSeek ? (int)(image.Length - image.Position) : 0);
            await image.CopyToAsync(buffer, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            var testRun = TestRun(name, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge);
            return Result(await Client.PostTestRunMultipart(
                testRun, buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken ?? CancellationToken.None).ConfigureAwait(false));
        }

        public async Task<TestRunResult> Track(
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
            IEnumerable<IgnoreAreaDto> ignoreAreas = null,
            bool? merge = null)
        {
            var testRun = TestRun(name, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge);
            return Result(await Client.PostTestRunMultipart(testRun, image, cancellationToken ?? CancellationToken.None).ConfigureAwait(false));
        }

        public async Task<TestRunResult> Track(
            string name,
            FileInfo image,
            CancellationToken? cancellationToken = null,
            string os = null,
            string browser = null,
            string viewport = null,
            string device = null,
            string customTags = null,
            double? diffTollerancePercent = null,
            string comment = null,
            IEnumerable<IgnoreAreaDto> ignoreAreas = null,
            bool? merge = null)
        {
            var bytes = await File.ReadAllBytesAsync(image.FullName, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

            return await Track(name, bytes, cancellationToken, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge).ConfigureAwait(false);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public Task<TestRunResult> Track(
            string name,
            string imageBase64,
            CancellationToken? cancellationToken,
            string os,
            string browser,
            string viewport,
            string device,
            string customTags,
            double? diffTollerancePercent,
            string comment,
            IEnumerable<IgnoreAreaDto> ignoreAreas)
        {
            return Track(name, imageBase64, cancellationToken, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge: null);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public Task<TestRunResult> Track(
            string name,
            Stream image,
            CancellationToken? cancellationToken,
            string os,
            string browser,
            string viewport,
            string device,
            string customTags,
            double? diffTollerancePercent,
            string comment,
            IEnumerable<IgnoreAreaDto> ignoreAreas)
        {
            return Track(name, image, cancellationToken, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge: null);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public Task<TestRunResult> Track(
            string name,
            byte[] image,
            CancellationToken? cancellationToken,
            string os,
            string browser,
            string viewport,
            string device,
            string customTags,
            double? diffTollerancePercent,
            string comment,
            IEnumerable<IgnoreAreaDto> ignoreAreas)
        {
            return Track(name, image, cancellationToken, os, browser, viewport, device, customTags, diffTollerancePercent, comment, ignoreAreas, merge: null);
        }

        private CreateTestRequestDto TestRun(
            string name,
            string os,
            string browser,
            string viewport,
            string device,
            string customTags,
            double? diffTollerancePercent,
            string comment,
            IEnumerable<IgnoreAreaDto> ignoreAreas,
            bool? merge)
        {
            EnsureStarted();

            return new CreateTestRequestDto
            {
                Name = name,
                BuildId = buildId,
                ProjectId = projectId,
                BranchName = config.BranchName,
                SourceBranch = config.SourceBranch,
                Os = os,
                Browser = browser,
                Viewport = viewport,
                Device = device,
                CustomTags = customTags,
                DiffTollerancePercent = diffTollerancePercent,
                Comment = comment,
                IgnoreAreas = ignoreAreas,
                Merge = merge
            };
        }

        private TestRunResult Result(TestRunResultDto response)
        {
            var result = new TestRunResult
            {
                Status = ParseStatus(response.Status),
                Url = response.Url,
                ImageUrl = Client.ImageUrl(response.ImageName),
                DiffUrl = Client.ImageUrl(response.DiffName),
                BaselineUrl = Client.ImageUrl(response.BaselineName),
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
            if (result.Status == TestRunStatus.Unknown) {
                Console.Error.WriteLine($"Unknown status '{response.Status}', update the VisualRegressionTracker package: {result.Url}");
            }

            return result;
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
