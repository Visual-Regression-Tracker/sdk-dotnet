using System.Net.Http;
using System.Text;
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
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
                Content = new StringContent(JsonSerializer.Serialize(body, jsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("apiKey", apiKey);
            request.Headers.Add("project", project);

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ServerError(response.StatusCode, ServerMessage(content), $"{method} {path}");
                }
                return JsonSerializer.Deserialize<T>(content, jsonOptions);
            }
            catch (HttpRequestException ex)
            {
                throw new VisualRegressionTrackerError(ex);
            }
            catch (JsonException ex)
            {
                throw new VisualRegressionTrackerError(ex);
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
