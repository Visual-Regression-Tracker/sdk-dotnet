using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VisualRegressionTracker
{
    internal sealed class ApiClient
    {
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // the default encoder escapes every '+' of a base64 image as \u002B
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private const int MaxRetries = 3;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly HttpClient httpClient;
        private readonly string apiUrl;
        private readonly string apiKey;
        private readonly string project;

        public ApiClient(HttpClient httpClient, string apiUrl, string apiKey, string project)
        {
            this.httpClient = httpClient;
            this.apiUrl = apiUrl.TrimEnd('/');
            this.apiKey = apiKey;
            this.project = project;
        }

        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

        public string ImageUrl(string imageName)
        {
            return string.IsNullOrEmpty(imageName) ? null : $"{apiUrl}/images/{imageName}";
        }

        public Task<BuildDto> CreateBuild(CreateBuildDto build, CancellationToken cancellationToken)
        {
            return Send<BuildDto>(HttpMethod.Post, "/builds", build, cancellationToken);
        }

        public Task<BuildDto> StopBuild(string buildId, CancellationToken cancellationToken)
        {
            return Send<BuildDto>(HttpMethod.Patch, $"/builds/{buildId}", new ModifyBuildDto(IsRunning: false), cancellationToken);
        }

        public Task<TestRunResultDto> PostTestRun(CreateTestRequestDto testRun, CancellationToken cancellationToken)
        {
            return Send<TestRunResultDto>(HttpMethod.Post, "/test-runs", testRun, cancellationToken);
        }

        public Task<TestRunResultDto> PostTestRunMultipart(
            CreateTestRequestDto testRun, ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
        {
            return Send<TestRunResultDto>(HttpMethod.Post, "/test-runs/multipart", () => Multipart(testRun, image), cancellationToken);
        }

        private Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(body, jsonOptions);
            return Send<T>(method, path, () =>
            {
                var content = new ByteArrayContent(json);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                return content;
            }, cancellationToken);
        }

        private static MultipartFormDataContent Multipart(CreateTestRequestDto testRun, ReadOnlyMemory<byte> image)
        {
            var form = new MultipartFormDataContent();
            foreach (var (name, value) in JsonSerializer.SerializeToNode(testRun, jsonOptions).AsObject())
            {
                var text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString(jsonOptions);
                form.Add(new StringContent(text), name);
            }
            var file = new ReadOnlyMemoryContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "image", "image.png");
            return form;
        }

        private async Task<T> Send<T>(HttpMethod method, string path, Func<HttpContent> content, CancellationToken cancellationToken)
        {
            long attemptStarted = 0;
            try
            {
                for (var retries = 0; ; retries++)
                {
                    attemptStarted = Stopwatch.GetTimestamp();
                    TimeSpan delay;
                    using (var request = new HttpRequestMessage(method, apiUrl + path) { Content = content() })
                    {
                        request.Headers.Add("apiKey", apiKey);
                        request.Headers.Add("project", project);

                        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                        if (retries >= MaxRetries || RetryDelay(response) is not TimeSpan retryDelay)
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                                throw new ServerError(
                                    response.StatusCode, ServerMessage(body), $"{method} {path}", request.Content is MultipartFormDataContent);
                            }
                            return await response.Content.ReadFromJsonAsync<T>(jsonOptions, cancellationToken).ConfigureAwait(false);
                        }
                        delay = retryDelay;
                    }
                    await Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // a connect timeout also surfaces as TaskCanceledException, but before HttpClient.Timeout elapses
                throw new VisualRegressionTrackerError(
                    httpClient.Timeout == Timeout.InfiniteTimeSpan || Stopwatch.GetElapsedTime(attemptStarted) < httpClient.Timeout
                        ? $"{method} {path} could not connect to {apiUrl}: {ex.InnerException?.Message ?? ex.Message}"
                        : $"{method} {path} timed out after {httpClient.Timeout}",
                    ex);
            }
            catch (JsonException ex)
            {
                throw new VisualRegressionTrackerError(
                    $"{method} {path} returned a response that is not from the VRT API; check that ApiUrl points to the API, not the web app", ex);
            }
        }

        private static TimeSpan? RetryDelay(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || retryAfter == null)
            {
                return null;
            }
            var delay = retryAfter.Delta ?? retryAfter.Date - DateTimeOffset.UtcNow ?? TimeSpan.Zero;
            return TimeSpan.FromTicks(Math.Clamp(delay.Ticks, 0, MaxRetryDelay.Ticks));
        }

        private static string ServerMessage(string content)
        {
            try
            {
                return JsonSerializer.Deserialize<ErrorDto>(content, jsonOptions)?.Message ?? content;
            }
            catch (JsonException)
            {
                return content;
            }
        }
    }
}
