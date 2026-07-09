using System.Net;

namespace PromptOps.Plugins.Tests.Fakes;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public static StubHttpMessageHandler ReturningJson(HttpStatusCode statusCode, string json) => new(request => new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(respond(request));
    }
}
