using Wh40kArmyEnricher.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Wh40kArmyEnricher.Core;

/// <summary>Produces YamlDotNet serialisers pre-configured for simulation profile output.</summary>
public static class YamlSerialiser
{
    public static ISerializer CreateSerializer()
    {
        return new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ScalarValueConverter())
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    public static IDeserializer CreateDeserialiser()
    {
        return new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ScalarValueConverter())
            .Build();
    }

    public static string Serialise<T>(T value) => CreateSerializer().Serialize(value);
}
