namespace JitenMPV.Core.Net;

/// Consulted per request rather than at construction, so every client in the process leaves the
/// curl fallback the moment the probe verifies in-process TLS.
public sealed class AdaptiveTlsHandler : HttpMessageHandler
{
    private readonly HttpMessageInvoker _curl = new(new CurlHttpHandler(), disposeHandler: true);
    private readonly HttpMessageInvoker _inProcess = new(JitenHttp.NewSocketsHandler(), disposeHandler: true);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => (JitenHttp.UseCurl ? _curl : _inProcess).SendAsync(request, ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _curl.Dispose();
            _inProcess.Dispose();
        }

        base.Dispose(disposing);
    }
}
