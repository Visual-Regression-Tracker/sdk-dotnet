using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Xunit;
using Moq; 
using Moq.Protected;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VisualRegressionTracker.Tests
{
    public static class Helpers
    {
        public static void SetupRequest<TReq, TResp>(
            this Mock<HttpMessageHandler> mock,
            HttpMethod expectedMethod,
            string expectedUrl,
            TReq expectedRequest,
            TResp responseDto)
        {
            Action<HttpRequestMessage, CancellationToken> callback = (request, ct) => 
            {
                var expectedJson = expectedRequest != null
                    ? JsonConvert.SerializeObject(expectedRequest)
                    : null;
                var actualJson = request.Content.ReadAsString();

                Assert.Equal(expectedMethod, request.Method);
                Assert.Equal(expectedUrl, request.RequestUri.ToString());
                Assert.Equal(expectedJson, actualJson);
            };

            var responseJson = JsonConvert.SerializeObject(responseDto);
            mock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(responseJson)
                })
                .Callback<HttpRequestMessage, CancellationToken>(callback)
                .Verifiable();
        }

        public static void SetupRequest(
            this Mock<HttpMessageHandler> mock,
            HttpMethod expectedMethod,
            string expectedUrl)
        {
            SetupRequest<string, string>(mock, expectedMethod, expectedUrl, null, null);
        }

        public static void SetupRequest(
            this Mock<HttpMessageHandler> mock,
            Exception exception)
        {
            mock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ThrowsAsync(exception)
                .Verifiable();
        }

        public static void VerifyRequest(
            this Mock<HttpMessageHandler> mock,
            int times,
            Action<HttpRequestMessage> match)
        {
            Func<HttpRequestMessage, bool> func = req => { match(req); return true; };

            mock.Protected().Verify(
                "SendAsync",
                Times.Exactly(times),
                ItExpr.Is<HttpRequestMessage>(req => func(req)),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        public static string ReadAsString(this HttpContent content) 
        {
            if (content == null) return null;
            var result =  new StreamReader(content.ReadAsStream()).ReadToEnd();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        public static object InvokeGeneric(
            this object obj, 
            string methodName,
            Type[] typeArgs,
            params object[] args)
        {
            var method = obj.GetType().GetMethod(methodName).MakeGenericMethod(typeArgs);
            var result = method.Invoke(obj, args);
            return result;
        }
    }
}