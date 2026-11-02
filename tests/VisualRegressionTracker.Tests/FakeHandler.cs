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
    public record RecordedRequest(HttpMethod Method, string Url, string ApiKey, string Project, string Body);

    public sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> responses = new();

        public List<RecordedRequest> Requests { get; } = new();

        public RecordedRequest LastRequest => Requests[^1];

        public void Respond(HttpStatusCode status, string body = "", string mediaType = "application/json")
        {
            responses.Enqueue(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
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
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responses.Dequeue()();
        }

        private static string Header(HttpRequestMessage request, string name)
        {
            return request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
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
