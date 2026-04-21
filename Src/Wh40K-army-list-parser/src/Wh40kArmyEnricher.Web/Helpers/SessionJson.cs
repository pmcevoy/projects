using System.Text.Json;
using Wh40kArmyEnricher.Core.Contracts;

namespace Wh40kArmyEnricher.Web.Helpers;

public static class SessionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ScalarValueJsonConverter() },
    };
}
