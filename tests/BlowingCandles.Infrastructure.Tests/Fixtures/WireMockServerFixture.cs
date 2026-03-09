using WireMock.Server;

namespace BlowingCandles.Infrastructure.Tests.Fixtures;

public sealed class WireMockServerFixture : IDisposable
{
    public WireMockServerFixture()
    {
        Server = WireMockServer.Start();
        BaseUri = new Uri(Server.Url!);
    }

    public WireMockServer Server { get; }

    public Uri BaseUri { get; }

    public void Reset()
    {
        Server.Reset();
    }

    public void Dispose()
    {
        Server.Dispose();
    }
}
