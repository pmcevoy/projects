using Wh40kArmyEnricher.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
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
            .DisableAliases()
            .WithEventEmitter(next => new LiteralBlockScalarEmitter(next))
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

/// <summary>
/// Forces multi-line strings to use YAML literal block scalar style (|) instead of
/// folded (>-). In folded style a single newline must be doubled in the file to survive
/// the round-trip; literal style preserves newlines exactly as-is.
/// </summary>
file sealed class LiteralBlockScalarEmitter(IEventEmitter next) : ChainedEventEmitter(next)
{
    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (eventInfo.Source.Type == typeof(string)
            && eventInfo.Source.Value is string str
            && str.Contains('\n'))
        {
            eventInfo.Style = ScalarStyle.Literal;
        }
        base.Emit(eventInfo, emitter);
    }
}
