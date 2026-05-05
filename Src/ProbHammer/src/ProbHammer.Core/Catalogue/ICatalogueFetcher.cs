namespace ProbHammer.Core.Catalogue;

public interface ICatalogueFetcher
{
    Task<IReadOnlyList<CatalogueFileInfo>> GetCatalogueListAsync(CancellationToken ct = default);
    Task<byte[]> FetchRawAsync(string filename, bool forceRefresh = false, CancellationToken ct = default);
}

public record CatalogueFileInfo(string Name);
