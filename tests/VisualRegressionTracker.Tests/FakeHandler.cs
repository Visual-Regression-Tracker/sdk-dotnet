using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace VisualRegressionTracker.Tests
{
    public record RecordedRequest(
        HttpMethod Method, string Url, string ApiKey, string Project, string Body, IReadOnlyDictionary<string, FormPart> Form);

    public record FormPart(byte[] Bytes, string FileName, string ContentType)
    {
        public string Value => Encoding.UTF8.GetString(Bytes);
    }

    public sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> responses = new();

        public List<RecordedRequest> Requests { get; } = new();

        public RecordedRequest LastRequest => Requests[^1];

        public void Respond(HttpStatusCode status, string body = "", string mediaType = "application/json", string retryAfter = null)
        {
            responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
                if (retryAfter != null)
                {
                    response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                }
                return response;
            });
        }

        public void Throw(Exception exception)
        {
            responses.Enqueue(() => throw exception);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri.ToString(),
                Header(request, "apiKey"),
                Header(request, "project"),
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Content is MultipartFormDataContent form ? await Parts(form, cancellationToken) : null));
            return responses.Dequeue()();
        }

        private static async Task<IReadOnlyDictionary<string, FormPart>> Parts(
            MultipartFormDataContent form, CancellationToken cancellationToken)
        {
            var parts = new Dictionary<string, FormPart>();
            foreach (var part in form)
            {
                var bytes = await part.ReadAsByteArrayAsync(cancellationToken);
                parts.Add(part.Headers.ContentDisposition.Name.Trim('"'), new FormPart(
                    bytes,
                    part.Headers.ContentDisposition.FileName?.Trim('"'),
                    part.Headers.ContentType?.MediaType));
            }
            return parts;
        }

        private static string Header(HttpRequestMessage request, string name)
        {
            return request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
        }
    }

    public sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    public sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] buffer) : base(buffer) { }

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }

    public static class JsonAssert
    {
        public static void Equal(string expected, string actual)
        {
            var expectedNode = JsonNode.Parse(expected);
            var actualNode = JsonNode.Parse(actual);
            Assert.True(
                JsonNode.DeepEquals(expectedNode, actualNode),
                $"Expected {expectedNode.ToJsonString()}\nActual   {actualNode.ToJsonString()}");
        }
    }
}
