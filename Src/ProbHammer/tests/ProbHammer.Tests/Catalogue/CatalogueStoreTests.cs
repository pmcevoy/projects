using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProbHammer.Core.Catalogue;

namespace ProbHammer.Tests.Catalogue;

public class CatalogueStoreTests
{
    private static readonly string CatalogueNs = "http://www.battlescribe.net/schema/catalogueSchema";

    private static byte[] XmlBytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private static byte[] MakeCatalogueBytes(string id, string name, int revision = 1,
        string entriesXml = "") =>
        XmlBytes($"""
            <catalogue xmlns="{CatalogueNs}"
                       id="{id}" name="{name}" revision="{revision}">
              <sharedSelectionEntries>{entriesXml}</sharedSelectionEntries>
            </catalogue>
            """);

    private static CatalogueStore BuildStore(Mock<ICatalogueFetcher> fetcherMock) =>
        new CatalogueStore(fetcherMock.Object, NullLogger<CatalogueStore>.Instance);

    // ── IsInitialised ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InitialiseAsync_SetsIsInitialised()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(Array.Empty<CatalogueFileInfo>());

        var store = BuildStore(fetcher);
        store.IsInitialised.Should().BeFalse();

        await store.InitialiseAsync();

        store.IsInitialised.Should().BeTrue();
    }

    // ── Loads all catalogues ────────────────────────────────────────────────────

    [Fact]
    public async Task InitialiseAsync_LoadsAllCataloguesFromFetcher()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[]
               {
                   new CatalogueFileInfo("space-marines.cat"),
                   new CatalogueFileInfo("chaos.cat"),
               });

        fetcher.Setup(f => f.FetchRawAsync("space-marines.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("sm-1", "Space Marines"));

        fetcher.Setup(f => f.FetchRawAsync("chaos.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("ch-1", "Chaos"));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        var catalogues = store.GetAllCatalogues();
        catalogues.Should().HaveCount(2);
        catalogues.Select(c => c.Name).Should().Contain("Space Marines");
        catalogues.Select(c => c.Name).Should().Contain("Chaos");
    }

    // ── GetCatalogue by id ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetCatalogue_ReturnsCatalogueById()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[] { new CatalogueFileInfo("test.cat") });

        fetcher.Setup(f => f.FetchRawAsync("test.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("cat-99", "TestCat", revision: 3));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        var cat = store.GetCatalogue("cat-99");
        cat.Should().NotBeNull();
        cat!.Name.Should().Be("TestCat");
        cat.Revision.Should().Be(3);
    }

    [Fact]
    public async Task GetCatalogue_ReturnsNullForUnknownId()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(Array.Empty<CatalogueFileInfo>());

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        store.GetCatalogue("nonexistent").Should().BeNull();
    }

    // ── GetAllTopLevelEntries ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTopLevelEntries_ReturnsEntriesAcrossAllCatalogues()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[]
               {
                   new CatalogueFileInfo("a.cat"),
                   new CatalogueFileInfo("b.cat"),
               });

        var entryA = """<selectionEntry id="e1" name="Space Marine" type="model" />""";
        var entryB = """<selectionEntry id="e2" name="Plague Marine" type="model" />""";

        fetcher.Setup(f => f.FetchRawAsync("a.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("a-1", "CatA", entriesXml: entryA));

        fetcher.Setup(f => f.FetchRawAsync("b.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("b-1", "CatB", entriesXml: entryB));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        var entries = store.GetAllTopLevelEntries();
        entries.Select(e => e.Name).Should().Contain("Space Marine");
        entries.Select(e => e.Name).Should().Contain("Plague Marine");
    }

    // ── Two-pass cross-catalogue infoLink resolution ────────────────────────────

    [Fact]
    public async Task InitialiseAsync_TwoPass_ResolvesSharedProfilesAcrossCatalogues()
    {
        // Catalogue A contributes a shared profile for an invulnerable save.
        // Catalogue B has a unit that references it via infoLink.
        // The store must resolve it correctly via the two-pass load.

        var catAXml = $"""
            <catalogue xmlns="{CatalogueNs}" id="cat-a" name="CatA" revision="1">
              <sharedProfiles>
                <profile id="invuln-profile" name="Invulnerable Save" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">4+ invulnerable save</characteristic>
                  </characteristics>
                </profile>
              </sharedProfiles>
            </catalogue>
            """;

        var catBXml = $"""
            <catalogue xmlns="{CatalogueNs}" id="cat-b" name="CatB" revision="1">
              <sharedSelectionEntries>
                <selectionEntry id="u1" name="Terminator" type="model">
                  <infoLinks>
                    <infoLink id="il1" name="Invulnerable Save" type="profile"
                              targetId="invuln-profile" />
                  </infoLinks>
                </selectionEntry>
              </sharedSelectionEntries>
            </catalogue>
            """;

        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[]
               {
                   new CatalogueFileInfo("cat-a.cat"),
                   new CatalogueFileInfo("cat-b.cat"),
               });

        fetcher.Setup(f => f.FetchRawAsync("cat-a.cat", false, default))
               .ReturnsAsync(XmlBytes(catAXml));

        fetcher.Setup(f => f.FetchRawAsync("cat-b.cat", false, default))
               .ReturnsAsync(XmlBytes(catBXml));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        var terminator = store.GetAllTopLevelEntries()
                              .SingleOrDefault(e => e.Name == "Terminator");
        terminator.Should().NotBeNull();
        terminator!.EntryInvulnerableSave.Should().Be(4);
    }

    // ── RefreshCataloguesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RefreshCataloguesAsync_RedownloadsOnlySpecifiedCatalogue()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[]
               {
                   new CatalogueFileInfo("sm.cat"),
                   new CatalogueFileInfo("chaos.cat"),
               });

        fetcher.Setup(f => f.FetchRawAsync("sm.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("sm-1", "SM", revision: 1));

        fetcher.Setup(f => f.FetchRawAsync("chaos.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("ch-1", "Chaos", revision: 1));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        // Set up an updated version of sm.cat
        fetcher.Setup(f => f.FetchRawAsync("sm.cat", true, default))
               .ReturnsAsync(MakeCatalogueBytes("sm-1", "SM Updated", revision: 2));

        await store.RefreshCataloguesAsync(new[] { "sm-1" });

        store.GetCatalogue("sm-1")!.Revision.Should().Be(2);
        store.GetCatalogue("sm-1")!.Name.Should().Be("SM Updated");
        // Chaos should be unchanged
        store.GetCatalogue("ch-1")!.Revision.Should().Be(1);
    }

    [Fact]
    public async Task RefreshCataloguesAsync_UsesForceRefreshTrue()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[] { new CatalogueFileInfo("sm.cat") });

        fetcher.Setup(f => f.FetchRawAsync("sm.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("sm-1", "SM", revision: 1));

        fetcher.Setup(f => f.FetchRawAsync("sm.cat", true, default))
               .ReturnsAsync(MakeCatalogueBytes("sm-1", "SM", revision: 2));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();
        await store.RefreshCataloguesAsync(new[] { "sm-1" });

        // Verify forceRefresh: true was used (revision came from the force-refresh mock)
        store.GetCatalogue("sm-1")!.Revision.Should().Be(2);
        fetcher.Verify(f => f.FetchRawAsync("sm.cat", true, default), Times.Once);
    }

    [Fact]
    public async Task RefreshCataloguesAsync_IgnoresUnknownCatalogueId()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(Array.Empty<CatalogueFileInfo>());

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        // Should not throw — just log a warning
        var act = async () => await store.RefreshCataloguesAsync(new[] { "unknown-id" });
        await act.Should().NotThrowAsync();
    }

    // ── Faulty catalogue file is skipped ────────────────────────────────────────

    [Fact]
    public async Task InitialiseAsync_SkipsMalformedCatalogue_LoadsRest()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher.Setup(f => f.GetCatalogueListAsync(default))
               .ReturnsAsync(new[]
               {
                   new CatalogueFileInfo("good.cat"),
                   new CatalogueFileInfo("bad.cat"),
               });

        fetcher.Setup(f => f.FetchRawAsync("good.cat", false, default))
               .ReturnsAsync(MakeCatalogueBytes("good-1", "Good Cat"));

        fetcher.Setup(f => f.FetchRawAsync("bad.cat", false, default))
               .ReturnsAsync(XmlBytes("this is not valid xml <<<"));

        var store = BuildStore(fetcher);
        await store.InitialiseAsync();

        store.IsInitialised.Should().BeTrue();
        store.GetAllCatalogues().Should().HaveCount(1);
        store.GetAllCatalogues()[0].Name.Should().Be("Good Cat");
    }
}
