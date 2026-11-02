using System.Net;

namespace VisualRegressionTracker
{
    public class ServerError : VisualRegressionTrackerError
    {
        public ServerError(HttpStatusCode statusCode, string serverMessage, string request)
            : base($"{request} failed with {(int)statusCode}: {serverMessage}{Hint(statusCode)}")
        {
            StatusCode = statusCode;
            ServerMessage = serverMessage;
        }

        public HttpStatusCode StatusCode { get; }
        public string ServerMessage { get; }

        private static string Hint(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.RequestEntityTooLarge =>
                    ". The request is above the server's JSON body limit (BODY_PARSER_JSON_LIMIT): raise it on the server or send a smaller image.",
                HttpStatusCode.UnprocessableEntity =>
                    ". The image has more pixels than the server allows (MAX_IMAGE_PIXELS): raise it on the server or send a smaller image.",
                _ => ""
            };
        }
    }
}
