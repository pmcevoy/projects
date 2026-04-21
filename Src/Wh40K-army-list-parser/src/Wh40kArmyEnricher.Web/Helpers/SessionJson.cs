using System.Text.Json;

namespace Wh40kArmyEnricher.Web.Helpers;

public static class SessionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
