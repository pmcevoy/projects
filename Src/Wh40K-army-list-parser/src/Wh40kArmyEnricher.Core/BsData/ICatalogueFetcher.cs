namespace Wh40kArmyEnricher.Core.BsData;

public interface ICatalogueFetcher
{
    /// <summary>Returns a readable stream for the named BSData catalogue file (XML or compressed).</summary>
    Task<Stream> FetchAsync(string filename, bool forceRefresh, CancellationToken ct = default);

    /// <summary>Fetches the raw body of any URL as a string (used for the GitHub contents API).</summary>
    Task<string> FetchRawAsync(string url, CancellationToken ct = default);
}
