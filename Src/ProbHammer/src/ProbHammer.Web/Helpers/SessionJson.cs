using System.Text.Json;
using ProbHammer.Core.Contracts;

namespace ProbHammer.Web.Helpers;

public static class SessionJson
{
    // Used for session storage — must NOT have PropertyNamingPolicy = CamelCase (breaks WeaponAbilities booleans)
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ScalarValueJsonConverter() },
    };

    // Used for data-unit HTML attributes — JavaScript expects camelCase property names
    public static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new ScalarValueJsonConverter() },
    };
}
