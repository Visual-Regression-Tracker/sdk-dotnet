using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
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

        public TimeSpan Timeout => httpClient.Timeout;

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

        private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, apiUrl + path)
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, jsonOptions))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.Add("apiKey", apiKey);
            request.Headers.Add("project", project);

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new ServerError(response.StatusCode, ServerMessage(content), $"{method} {path}");
                }
                return await response.Content.ReadFromJsonAsync<T>(jsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new VisualRegressionTrackerError($"{method} {path} timed out after {httpClient.Timeout}", ex);
            }
            catch (JsonException ex)
            {
                throw new VisualRegressionTrackerError(
                    $"{method} {path} returned a response that is not from the VRT API; check that ApiUrl points to the API, not the web app", ex);
            }
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
