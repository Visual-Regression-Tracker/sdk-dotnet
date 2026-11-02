using System.Net;

namespace VisualRegressionTracker
{
    public class ServerError : VisualRegressionTrackerError
    {
        public ServerError(HttpStatusCode statusCode, string serverMessage, string request)
            : this(statusCode, serverMessage, request, multipart: false)
        {
        }

        internal ServerError(HttpStatusCode statusCode, string serverMessage, string request, bool multipart)
            : base($"{request} failed with {(int)statusCode}{(string.IsNullOrEmpty(serverMessage) ? "" : $": {serverMessage}")}{Hint(statusCode, multipart)}")
        {
            StatusCode = statusCode;
            ServerMessage = serverMessage;
        }

        public HttpStatusCode StatusCode { get; }
        public string ServerMessage { get; }

        private static string Hint(HttpStatusCode statusCode, bool multipart)
        {
            return statusCode switch
            {
                HttpStatusCode.RequestEntityTooLarge when multipart =>
                    ". The image is above the server's upload limit (MULTIPART_FILE_SIZE_LIMIT): raise it on the server or send a smaller image.",
                HttpStatusCode.RequestEntityTooLarge =>
                    ". The request is above the server's JSON body limit (BODY_PARSER_JSON_LIMIT): raise it on the server, pass the image as bytes, a Stream or a file to send it as multipart, or send a smaller image.",
                HttpStatusCode.UnprocessableEntity =>
                    ". Raise MAX_IMAGE_PIXELS on the server or send a smaller image.",
                _ => ""
            };
        }
    }
}
