# BSData Parsing & Catalogue Loading

## Data Source

`https://github.com/BSData/wh40k-10e` — approximately 46 `.cat` files, ~35 MB total.

---

## Fetching Strategy

Download everything on first run rather than attempting faction-to-filename mapping (fragile — link names frequently differ from actual filenames).

1. Load `Warhammer 40,000.gst` (game system root)
2. Fetch full file listing: `GET https://api.github.com/repos/BSData/wh40k-10e/contents/` — cache to `~/.wh40k-enricher/cache/catalogue-list.json`
3. Download and parse each `.cat` file — cache each to `~/.wh40k-enricher/cache/{filename}`
4. All catalogues remain in memory for the lifetime of the run; no lazy loading

**Do not** use the GitHub Commits API for staleness checking — aggressively rate-limited even for unauthenticated reads.

Re-downloading specific catalogues at runtime: `POST /api/refresh-catalogues` (reads `used_catalogue_ids` from session, calls `CatalogueStore.RefreshCataloguesAsync`).

---

## File Format

UTF-8 XML, namespace `http://www.battlescribe.net/schema/catalogueSchema`.

`.catz` variant is zlib-compressed (raw deflate, no header):

```csharp
using var deflate = new DeflateStream(rawStream, CompressionMode.Decompress);
var doc = await XDocument.LoadAsync(deflate, LoadOptions.None, ct);
```

**Do not** use `ZLibStream` or `GZipStream` for `.catz` files.

Declare the namespace constant once in `CatalogueParser.cs`:

```csharp
private static readonly XNamespace Ns =
    "http://www.battlescribe.net/schema/catalogueSchema";
```

---

## XML Structure

Key containers at catalogue root level:

- `<selectionEntries>` — force/army roster entries
- `<sharedSelectionEntries>` — unit and model datasheets; **must be included in top-level entry search**
- `<sharedSelectionEntryGroups>` — wargear option groups; **must also be parsed**

Within each `selectionEntry`, child entries live in:

- `<selectionEntries>` — direct child model/upgrade entries
- `<selectionEntryGroups>` — wargear option groups; **traverse recursively** (double nesting observed, e.g. Repulsor Executioner → Wargear → Turret Weapon → Heavy Laser Destroyer); stop at depth 6
- `<entryLinks>` — references to `<sharedSelectionEntries>` by `targetId`

`profile[@typeName]` values in 10th edition:
- `"Unit"` — model statline: M, T, Sv, W, Ld, OC
- `"Ranged Weapons"` — Range, A, BS, S, AP, D, Keywords
- `"Melee Weapons"` — Range (always "Melee"), A, WS, S, AP, D, Keywords
- `"Abilities"` — free-text special rules; capture name + text

**All `typeName` comparisons must use `StringComparison.OrdinalIgnoreCase`** — case variation has been observed in the wild.

### Squad unit vs single-model unit

```xml
<!-- Squad: statline on child model entries, not the squad entry itself -->
<selectionEntry id="abc-123" name="Assault Intercessor Squad" type="unit">
  <profiles><!-- Only ability profiles here --></profiles>
  <selectionEntryGroups>
    <selectionEntryGroup name="Assault Intercessors">
      <selectionEntries>
        <selectionEntry name="Assault Intercessor" type="model">
          <profiles>
            <profile name="Assault Intercessor" typeName="Unit">
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

<!-- Single-model: statline IS on the entry -->
<selectionEntry id="def-456" name="Foetid Bloat-drone" type="model">
  <profiles>
    <profile name="Foetid Bloat-drone" typeName="Unit">...</profile>
  </profiles>
</selectionEntry>

<!-- Multi-profile weapon -->
<selectionEntry name="Hellforged weapons" type="upgrade">
  <profiles>
    <profile name="➤ Hellforged weapons - strike" typeName="Melee Weapons">...</profile>
    <profile name="➤ Hellforged weapons - sweep" typeName="Melee Weapons">...</profile>
  </profiles>
</selectionEntry>
```

---

## Two-Pass Catalogue Load (Cross-Catalogue infoLinks)

`CatalogueStore.InitialiseAsync` does a two-pass load to handle cross-catalogue infoLink resolution (e.g. a Black Templars unit's infoLink may target a shared profile in `Imperium - Space Marines.cat`):

1. **Pass 1:** load all XDocuments and merge their `sharedProfiles` into a global `Dictionary<string, XElement>`
2. **Pass 2:** parse each document using that global map as a fallback so cross-catalogue `profileLink` and `infoLink` targets resolve correctly

`_globalProfiles` is retained as a field on `CatalogueStore` after `InitialiseAsync` completes — required by `RefreshCataloguesAsync`. Do not discard it or scope it to `InitialiseAsync` only.

---

## Invulnerable Saves and FNP Extraction

BSData 10e does **not** use game shorthand (`4++`, `5+++`) in ability text. Two storage patterns:

1. **Ability text** — `"N+ invulnerable save"` or `"N++ invulnerable save"` (regex `(\d)\+\+? invulnerable`) and `"Feel No Pain N+"` (regex `Feel No Pain (\d)\+`). Use the regex — do not match literal strings.
2. **infoLinks** — `<infoLink name="Invulnerable Save" type="profile">` pointing to a shared profile whose Description is `"4+"`, and `<infoLink name="Feel No Pain" type="rule">` with a `<modifier>` encoding the threshold.

Both patterns extracted by `CatalogueParser.ExtractInvulnFnp()`, stored on `CatalogueEntry.EntryInvulnerableSave` / `EntryFeelNoPain`. Set to `null` when absent.

**Single-model unit ability upgrades:** `unitEntry.Statline` is non-null for single-model units, so `defenderStatline` is pre-initialised before the model loop. Use a `defenderStatlineSet` boolean flag instead of a null-check guard — the first model's fully-enriched statline must always overwrite `defenderStatline`, including ability upgrades (e.g. Shield Dome → 5+ invuln) applied inside the loop.

---

## Catalogue Version Display and Selective Refresh

`Enricher.Enrich()` returns `(IReadOnlyList<EnrichedUnit> Units, IReadOnlySet<string> UsedCatalogueIds)`. After enrichment, IDs stored in `used_catalogue_ids` session key.

`CatalogueData` carries:
- `Revision` (int) — parsed from `revision` XML attribute on the catalogue root element
- `Filename` (string) — the BSData repository filename; stored via `catalogue with { Filename = filename }` during `InitialiseAsync`

`CatalogueStore.RefreshCataloguesAsync(IEnumerable<string> catalogueIds)` — re-downloads and re-parses specified catalogues by ID, bypassing disk cache (`forceRefresh: true`). Merges any updated shared profiles from refreshed documents back into `_globalProfiles`. Only works for catalogues whose `Filename` is known.

---

## HTTP / Caching

Use `IHttpClientFactory` with a named client. Set a `User-Agent` header — the GitHub API rejects requests without one. Register via DI.

GitHub raw URL pattern: `https://raw.githubusercontent.com/BSData/wh40k-10e/main/{Uri.EscapeDataString(filename)}` — spaces in filenames (e.g. `Imperium - Black Templars.cat`) must be encoded as `%20`.

Cache the catalogue file listing to `catalogue-list.json`. After the first run everything is read from disk; subsequent runs start fast.
