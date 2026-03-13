namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Downloads and caches BSData catalogue files from GitHub.
/// </summary>
public interface ICatalogueFetcher
{
    /// <summary>
    /// Returns a readable stream for the named catalogue file.
    /// The stream may come from disk cache or a fresh download.
    /// </summary>
    Task<Stream> FetchAsync(string filename, bool forceRefresh, CancellationToken ct = default);
}
