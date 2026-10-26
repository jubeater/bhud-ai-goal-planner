using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// An HTTP endpoint that does not exist: it answers with whatever a test tells it to, and keeps
    /// what the Provider client sent so a test can check the request.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
            : this(request => new HttpResponseMessage(statusCode) { Content = new StringContent(body ?? string.Empty) }) { }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) {
            _respond = respond ?? throw new ArgumentNullException(nameof(respond));
        }

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public List<string> RequestBodies { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);

            if (request.Content != null) RequestBodies.Add(await request.Content.ReadAsStringAsync().ConfigureAwait(false));

            return _respond(request);
        }
    }
}
