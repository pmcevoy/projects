using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Wh40kArmyEnricher.Core.Catalogue;
using Wh40kArmyEnricher.Core.Contracts;

namespace Wh40kArmyEnricher.Tests.Catalogue;

public class CatalogueParserTests
{
    private static readonly string CatalogueNs = "http://www.battlescribe.net/schema/catalogueSchema";

    // ── XML helpers ─────────────────────────────────────────────────────────────

    private static XDocument MakeCatalogue(string bodyXml, string id = "cat-1",
        string name = "Test Catalogue", int revision = 5)
    {
        return XDocument.Parse($"""
            <catalogue xmlns="{CatalogueNs}"
                       id="{id}" name="{name}" revision="{revision}">
              {bodyXml}
            </catalogue>
            """);
    }

    private static XDocument MakeCatalogueWithSharedEntries(string entriesXml) =>
        MakeCatalogue($"<sharedSelectionEntries>{entriesXml}</sharedSelectionEntries>");

    private static IReadOnlyDictionary<string, XElement> EmptyProfiles =>
        new Dictionary<string, XElement>();

    private static CatalogueData Parse(XDocument doc) =>
        CatalogueParser.Parse(doc, "test.cat", EmptyProfiles, NullLogger.Instance);

    // ── Catalogue metadata ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsCatalogueMetadata()
    {
        var doc = MakeCatalogue("", "cat-abc", "Test Catalogue", 42);
        var data = Parse(doc);

        data.Id.Should().Be("cat-abc");
        data.Name.Should().Be("Test Catalogue");
        data.Revision.Should().Be(42);
        data.Filename.Should().Be("test.cat");
    }

    // ── Statlines ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SingleModelUnit_ExtractsStatline()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Foetid Bloat-drone" type="model">
              <profiles>
                <profile id="p1" name="Foetid Bloat-drone" typeName="Unit">
                  <characteristics>
                    <characteristic name="T">8</characteristic>
                    <characteristic name="Sv">3+</characteristic>
                    <characteristic name="W">10</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();

        entry.Name.Should().Be("Foetid Bloat-drone");
        entry.EntryType.Should().Be("model");
        entry.Statline.Should().NotBeNull();
        entry.Statline!.Toughness.Should().Be(8);
        entry.Statline.Save.Should().Be(3);
        entry.Statline.Wounds.Should().Be(10);
    }

    [Fact]
    public void Parse_SquadUnit_ChildModelHasStatline()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="u1" name="Assault Intercessor Squad" type="unit">
              <selectionEntryGroups>
                <selectionEntryGroup name="Assault Intercessors">
                  <selectionEntries>
                    <selectionEntry id="m1" name="Assault Intercessor" type="model">
                      <profiles>
                        <profile id="p1" name="Assault Intercessor" typeName="Unit">
                          <characteristics>
                            <characteristic name="T">4</characteristic>
                            <characteristic name="Sv">3+</characteristic>
                            <characteristic name="W">2</characteristic>
                          </characteristics>
                        </profile>
                      </profiles>
                    </selectionEntry>
                  </selectionEntries>
                </selectionEntryGroup>
              </selectionEntryGroups>
            </selectionEntry>
            """);

        var squad = Parse(doc).Entries.Single();
        squad.Name.Should().Be("Assault Intercessor Squad");
        squad.Statline.Should().BeNull();

        var model = squad.Children.Single();
        model.Name.Should().Be("Assault Intercessor");
        model.Statline.Should().NotBeNull();
        model.Statline!.Toughness.Should().Be(4);
        model.Statline.Save.Should().Be(3);
        model.Statline.Wounds.Should().Be(2);
    }

    // ── Weapons — ranged ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_RangedWeapon_ExtractsAllStats()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="w1" name="Boltgun" type="upgrade">
              <profiles>
                <profile id="p1" name="Boltgun" typeName="Ranged Weapons">
                  <characteristics>
                    <characteristic name="Range">24"</characteristic>
                    <characteristic name="A">2</characteristic>
                    <characteristic name="BS">3+</characteristic>
                    <characteristic name="S">4</characteristic>
                    <characteristic name="AP">0</characteristic>
                    <characteristic name="D">1</characteristic>
                    <characteristic name="Keywords">-</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        var weapon = entry.Weapons.Single();

        weapon.Name.Should().Be("Boltgun");
        weapon.Type.Should().Be(WeaponType.Ranged);
        weapon.Range.Should().Be(24);
        weapon.Variants.Should().HaveCount(1);

        var v = weapon.Variants[0];
        v.VariantName.Should().Be("");
        v.AttacksRaw.Should().Be("2");
        v.Skill.Should().Be(3);
        v.Strength.Should().Be(4);
        v.Ap.Should().Be(0);
        v.DamageRaw.Should().Be("1");
    }

    // ── Weapons — melee ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_MeleeWeapon_NegativeAp()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="w1" name="Astartes Chainsword" type="upgrade">
              <profiles>
                <profile id="p1" name="Astartes Chainsword" typeName="Melee Weapons">
                  <characteristics>
                    <characteristic name="Range">Melee</characteristic>
                    <characteristic name="A">4</characteristic>
                    <characteristic name="WS">3+</characteristic>
                    <characteristic name="S">4</characteristic>
                    <characteristic name="AP">-1</characteristic>
                    <characteristic name="D">1</characteristic>
                    <characteristic name="Keywords">-</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var weapon = Parse(doc).Entries.Single().Weapons.Single();

        weapon.Type.Should().Be(WeaponType.Melee);
        weapon.Range.Should().Be(0);
        weapon.Variants[0].Ap.Should().Be(-1);
        weapon.Variants[0].Skill.Should().Be(3);
    }

    // ── Multi-profile weapon variants ───────────────────────────────────────────

    [Fact]
    public void Parse_MultiProfileWeapon_VariantLabelsStripped()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="w1" name="Hellforged weapons" type="upgrade">
              <profiles>
                <profile id="p1" name="&#x27A4; Hellforged weapons - strike" typeName="Melee Weapons">
                  <characteristics>
                    <characteristic name="Range">Melee</characteristic>
                    <characteristic name="A">4</characteristic>
                    <characteristic name="WS">3+</characteristic>
                    <characteristic name="S">8</characteristic>
                    <characteristic name="AP">-2</characteristic>
                    <characteristic name="D">2</characteristic>
                    <characteristic name="Keywords">-</characteristic>
                  </characteristics>
                </profile>
                <profile id="p2" name="&#x27A4; Hellforged weapons - sweep" typeName="Melee Weapons">
                  <characteristics>
                    <characteristic name="Range">Melee</characteristic>
                    <characteristic name="A">8</characteristic>
                    <characteristic name="WS">3+</characteristic>
                    <characteristic name="S">5</characteristic>
                    <characteristic name="AP">-1</characteristic>
                    <characteristic name="D">1</characteristic>
                    <characteristic name="Keywords">-</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var weapon = Parse(doc).Entries.Single().Weapons.Single();

        weapon.Name.Should().Be("Hellforged weapons");
        weapon.Variants.Should().HaveCount(2);
        weapon.Variants[0].VariantName.Should().Be("strike");
        weapon.Variants[0].Ap.Should().Be(-2);
        weapon.Variants[1].VariantName.Should().Be("sweep");
        weapon.Variants[1].Ap.Should().Be(-1);
    }

    // ── Weapon keywords ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Blast", true, false, false, false)]
    [InlineData("Torrent", false, true, false, false)]
    [InlineData("Lethal Hits", false, false, true, false)]
    [InlineData("Devastating Wounds", false, false, false, true)]
    public void Parse_WeaponKeyword_BoolAbilities(string keyword,
        bool blast, bool torrent, bool lethalHits, bool devastatingWounds)
    {
        var doc = MakeCatalogueWithSharedEntries($"""
            <selectionEntry id="w1" name="Gun" type="upgrade">
              <profiles>
                <profile id="p1" name="Gun" typeName="Ranged Weapons">
                  <characteristics>
                    <characteristic name="Range">24"</characteristic>
                    <characteristic name="A">1</characteristic>
                    <characteristic name="BS">3+</characteristic>
                    <characteristic name="S">4</characteristic>
                    <characteristic name="AP">0</characteristic>
                    <characteristic name="D">1</characteristic>
                    <characteristic name="Keywords">{keyword}</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var abilities = Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities;
        abilities.Blast.Should().Be(blast);
        abilities.Torrent.Should().Be(torrent);
        abilities.LethalHits.Should().Be(lethalHits);
        abilities.DevastatingWounds.Should().Be(devastatingWounds);
    }

    [Fact]
    public void Parse_RapidFireKeyword_ParsedCorrectly()
    {
        var doc = WeaponDocWithKeywords("Rapid Fire 2");
        Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities.RapidFire.Should().Be(2);
    }

    [Fact]
    public void Parse_SustainedHitsKeyword_ParsedCorrectly()
    {
        var doc = WeaponDocWithKeywords("Sustained Hits 1");
        Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities.SustainedHits.Should().Be(1);
    }

    [Fact]
    public void Parse_MeltaKeyword_ParsedCorrectly()
    {
        var doc = WeaponDocWithKeywords("Melta 2");
        Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities.Melta.Should().Be(2);
    }

    [Fact]
    public void Parse_TwinLinkedKeyword_ParsedCorrectly()
    {
        var doc = WeaponDocWithKeywords("Twin-linked");
        Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities.TwinLinked.Should().BeTrue();
    }

    [Fact]
    public void Parse_IndirectFireKeyword_ParsedCorrectly()
    {
        var doc = WeaponDocWithKeywords("Indirect Fire");
        Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities.IndirectFire.Should().BeTrue();
    }

    [Fact]
    public void Parse_AntiKeyword_ParsedCorrectly()
    {
        var doc = WeaponDocWithKeywords("Anti-INFANTRY 4+");
        var anti = Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities.Anti;
        anti.Should().ContainKey("INFANTRY").WhoseValue.Should().Be(4);
    }

    [Fact]
    public void Parse_MultipleKeywords_AllParsed()
    {
        var doc = WeaponDocWithKeywords("Blast, Lethal Hits, Rapid Fire 1");
        var abilities = Parse(doc).Entries.Single().Weapons.Single().Variants[0].Abilities;
        abilities.Blast.Should().BeTrue();
        abilities.LethalHits.Should().BeTrue();
        abilities.RapidFire.Should().Be(1);
    }

    // ── Invuln / FNP from ability text ──────────────────────────────────────────

    [Fact]
    public void Parse_InvulnFromAbilityText_Extracted()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Paladin Ancient" type="model">
              <profiles>
                <profile id="p1" name="Aegis" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">This model has a 4+ invulnerable save.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        Parse(doc).Entries.Single().EntryInvulnerableSave.Should().Be(4);
    }

    [Fact]
    public void Parse_InvulnDoublePlus_Extracted()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Magnus" type="model">
              <profiles>
                <profile id="p1" name="Sorcerous Aegis" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">Magnus has a 4++ invulnerable save.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        Parse(doc).Entries.Single().EntryInvulnerableSave.Should().Be(4);
    }

    [Fact]
    public void Parse_FnpFromAbilityText_Extracted()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Death Guard Champion" type="model">
              <profiles>
                <profile id="p1" name="Disgustingly Resilient" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">Feel No Pain 5+</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        Parse(doc).Entries.Single().EntryFeelNoPain.Should().Be(5);
    }

    // ── Invuln from cross-catalogue infoLink ────────────────────────────────────

    [Fact]
    public void Parse_InvulnFromInfoLink_ResolvedViaGlobalProfiles()
    {
        // Shared profile lives in catalogue A; unit with infoLink lives in catalogue B.
        var catA = XDocument.Parse($"""
            <catalogue xmlns="{CatalogueNs}" id="cat-a" name="Cat A" revision="1">
              <sharedProfiles>
                <profile id="shared-invuln" name="Invulnerable Save" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">4++ invulnerable save</characteristic>
                  </characteristics>
                </profile>
              </sharedProfiles>
            </catalogue>
            """);

        var catB = XDocument.Parse($"""
            <catalogue xmlns="{CatalogueNs}" id="cat-b" name="Cat B" revision="1">
              <sharedSelectionEntries>
                <selectionEntry id="u1" name="Terminator" type="model">
                  <infoLinks>
                    <infoLink id="il1" name="Invulnerable Save" type="profile"
                              targetId="shared-invuln" />
                  </infoLinks>
                </selectionEntry>
              </sharedSelectionEntries>
            </catalogue>
            """);

        // Pass 1: collect global profiles from both catalogues
        var globalProfiles = CatalogueParser.ExtractSharedProfiles(catA);
        foreach (var (k, v) in CatalogueParser.ExtractSharedProfiles(catB))
            globalProfiles.TryAdd(k, v);

        // Pass 2: parse catalogue B using global map
        var dataB = CatalogueParser.Parse(catB, "cat-b.cat", globalProfiles, NullLogger.Instance);

        dataB.Entries.Single().EntryInvulnerableSave.Should().Be(4);
    }

    // ── Keywords from <keywords> element ────────────────────────────────────────

    [Fact]
    public void Parse_EntryKeywords_Extracted()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Squad" type="unit">
              <keywords>
                <keyword name="INFANTRY" />
                <keyword name="CORE" />
              </keywords>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        entry.Keywords.Should().Contain("INFANTRY");
        entry.Keywords.Should().Contain("CORE");
    }

    // ── Abilities extracted ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_AbilityProfile_ExtractedOnEntry()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Space Marine" type="model">
              <profiles>
                <profile id="p1" name="And They Shall Know No Fear" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">This unit can ignore some morale effects.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        entry.Abilities.Should().HaveCount(1);
        entry.Abilities[0].Name.Should().Be("And They Shall Know No Fear");
        entry.Abilities[0].Text.Should().Contain("morale");
    }

    // ── Sub-ability profiles (non-standard typeName) ────────────────────────────

    [Fact]
    public void Parse_SubAbilityProfile_NoMatchingParent_CreatesNewAbility()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Death Guard Champion" type="model">
              <profiles>
                <profile id="p1" name="Plague Wind" typeName="Lord of the Death Guard">
                  <characteristics>
                    <characteristic name="Effect">Each time this unit makes an attack, on a Critical Hit, that attack has the [LETHAL HITS] ability.</characteristic>
                  </characteristics>
                </profile>
                <profile id="p2" name="Pestilential Mucus" typeName="Lord of the Death Guard">
                  <characteristics>
                    <characteristic name="Effect">Improve the Armour Penetration characteristic of melee weapons by 1.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        var ability = entry.Abilities.Should().ContainSingle(a => a.Name == "Lord of the Death Guard").Subject;
        ability.Text.Should().Contain("• Plague Wind:");
        ability.Text.Should().Contain("• Pestilential Mucus:");
    }

    [Fact]
    public void Parse_SubAbilityProfile_AppendsToMatchingParentAbility()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Plague Marine Champion" type="model">
              <profiles>
                <profile id="p1" name="Blessings of Nurgle" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">At the start of your Command phase, select one blessing.</characteristic>
                  </characteristics>
                </profile>
                <profile id="p2" name="Plague Wind" typeName="Blessings of Nurgle">
                  <characteristics>
                    <characteristic name="Effect">Critical Hits gain [LETHAL HITS].</characteristic>
                  </characteristics>
                </profile>
                <profile id="p3" name="Pestilential Mucus" typeName="Blessings of Nurgle">
                  <characteristics>
                    <characteristic name="Effect">Improve AP of melee weapons by 1.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        entry.Abilities.Should().HaveCount(1);
        var ability = entry.Abilities[0];
        ability.Name.Should().Be("Blessings of Nurgle");
        ability.Text.Should().StartWith("At the start of your Command phase");
        ability.Text.Should().Contain("\n• Plague Wind:");
        ability.Text.Should().Contain("\n• Pestilential Mucus:");
    }

    [Fact]
    public void Parse_SubAbilityProfile_AppearsBefore_ParentAbility_TwoPassHandlesCorrectly()
    {
        // Sub-ability profile appears BEFORE the matching Abilities profile in XML.
        // Single-pass would fail to find the parent; two-pass handles it correctly.
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Some Character" type="model">
              <profiles>
                <profile id="p2" name="Option A" typeName="My Choices">
                  <characteristics>
                    <characteristic name="Effect">Do thing A.</characteristic>
                  </characteristics>
                </profile>
                <profile id="p1" name="My Choices" typeName="Abilities">
                  <characteristics>
                    <characteristic name="Description">Choose one of the following.</characteristic>
                  </characteristics>
                </profile>
                <profile id="p3" name="Option B" typeName="My Choices">
                  <characteristics>
                    <characteristic name="Effect">Do thing B.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        entry.Abilities.Should().HaveCount(1);
        var ability = entry.Abilities[0];
        ability.Name.Should().Be("My Choices");
        ability.Text.Should().StartWith("Choose one of the following.");
        ability.Text.Should().Contain("• Option A:");
        ability.Text.Should().Contain("• Option B:");
    }

    [Fact]
    public void Parse_SubAbilityProfile_MultipleCharacteristics_JoinedWithEmDash()
    {
        var doc = MakeCatalogueWithSharedEntries("""
            <selectionEntry id="e1" name="Warrior" type="model">
              <profiles>
                <profile id="p1" name="Power Surge" typeName="Battle Stances">
                  <characteristics>
                    <characteristic name="Roll">4+</characteristic>
                    <characteristic name="Effect">Add 1 to Strength.</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

        var entry = Parse(doc).Entries.Single();
        var ability = entry.Abilities.Should().ContainSingle(a => a.Name == "Battle Stances").Subject;
        ability.Text.Should().Be("• Power Surge: 4+ — Add 1 to Strength.");
    }

    // ── entryLink resolution ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_EntryLink_ResolvesFromLocalSharedEntries()
    {
        var doc = MakeCatalogue("""
            <selectionEntries>
              <selectionEntry id="parent" name="Parent Squad" type="unit">
                <entryLinks>
                  <entryLink id="link1" targetId="shared-model" />
                </entryLinks>
              </selectionEntry>
            </selectionEntries>
            <sharedSelectionEntries>
              <selectionEntry id="shared-model" name="Shared Model" type="model">
                <profiles>
                  <profile id="p1" name="Shared Model" typeName="Unit">
                    <characteristics>
                      <characteristic name="T">5</characteristic>
                      <characteristic name="Sv">2+</characteristic>
                      <characteristic name="W">3</characteristic>
                    </characteristics>
                  </profile>
                </profiles>
              </selectionEntry>
            </sharedSelectionEntries>
            """);

        var parent = Parse(doc).Entries.First(e => e.Name == "Parent Squad");
        var child = parent.Children.Single();

        child.Name.Should().Be("Shared Model");
        child.Statline!.Toughness.Should().Be(5);
    }

    // ── .catz decompression ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadDocumentAsync_CatzFile_DecompressesCorrectly()
    {
        var xml = $"""
            <catalogue xmlns="{CatalogueNs}" id="cz-1" name="Compressed" revision="7">
              <sharedSelectionEntries>
                <selectionEntry id="e1" name="Test Entry" type="unit" />
              </sharedSelectionEntries>
            </catalogue>
            """;

        var rawBytes = RawDeflateCompress(Encoding.UTF8.GetBytes(xml));
        var doc = await CatalogueParser.LoadDocumentAsync(rawBytes, "test.catz");

        var data = CatalogueParser.Parse(doc, "test.catz", EmptyProfiles, NullLogger.Instance);
        data.Name.Should().Be("Compressed");
        data.Revision.Should().Be(7);
        data.Entries.Should().HaveCount(1);
        data.Entries[0].Name.Should().Be("Test Entry");
    }

    [Fact]
    public async Task LoadDocumentAsync_CatFile_LoadsDirectly()
    {
        var xml = $"""
            <catalogue xmlns="{CatalogueNs}" id="c1" name="Plain" revision="3" />
            """;
        var bytes = Encoding.UTF8.GetBytes(xml);
        var doc = await CatalogueParser.LoadDocumentAsync(bytes, "plain.cat");

        doc.Root.Should().NotBeNull();
        doc.Root!.Attribute("name")?.Value.Should().Be("Plain");
    }

    // ── Shared entries in top-level containers ───────────────────────────────────

    [Fact]
    public void Parse_SharedSelectionEntryGroups_IncludedAsEntries()
    {
        var doc = MakeCatalogue("""
            <sharedSelectionEntryGroups>
              <selectionEntryGroup name="Common Models">
                <selectionEntries>
                  <selectionEntry id="e1" name="Common Model" type="model" />
                </selectionEntries>
              </selectionEntryGroup>
            </sharedSelectionEntryGroups>
            """);

        Parse(doc).Entries.Should().Contain(e => e.Name == "Common Model");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static XDocument WeaponDocWithKeywords(string keywords) =>
        MakeCatalogueWithSharedEntries($"""
            <selectionEntry id="w1" name="Gun" type="upgrade">
              <profiles>
                <profile id="p1" name="Gun" typeName="Ranged Weapons">
                  <characteristics>
                    <characteristic name="Range">24"</characteristic>
                    <characteristic name="A">1</characteristic>
                    <characteristic name="BS">3+</characteristic>
                    <characteristic name="S">4</characteristic>
                    <characteristic name="AP">0</characteristic>
                    <characteristic name="D">1</characteristic>
                    <characteristic name="Keywords">{keywords}</characteristic>
                  </characteristics>
                </profile>
              </profiles>
            </selectionEntry>
            """);

    private static byte[] RawDeflateCompress(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
            deflate.Write(input, 0, input.Length);
        return ms.ToArray();
    }
}
