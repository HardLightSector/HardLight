using Robust.Shared.Reflection;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;
using System.Globalization;

namespace Content.Server.NPC;

public sealed class NPCBlackboardSerializer : ITypeReader<NPCBlackboard, MappingDataNode>, ITypeCopier<NPCBlackboard>
{
    private static readonly HashSet<string> SimpleBoolKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        NPCBlackboard.NavClimb,
        NPCBlackboard.NavInteract,
        NPCBlackboard.NavPry,
        NPCBlackboard.NavSmash,
    };

    public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        var validated = new List<ValidationNode>();

        if (node.Count <= 0)
            return new ValidatedSequenceNode(validated);

        var reflection = dependencies.Resolve<IReflectionManager>();

        foreach (var data in node)
        {
            // Normalize key for resilient matching
            var key = data.Key?.Trim() ?? string.Empty;

            if (data.Value.Tag == null)
            {
                // Allow plain booleans for common navigation toggles.
                if (SimpleBoolKeys.Contains(key))
                {
                    validated.Add(serializationManager.ValidateNode<bool>(data.Value, context));
                    continue;
                }

                // Fallback: accept untagged scalars as strings to avoid hard failures during validation.
                validated.Add(serializationManager.ValidateNode<string>(data.Value, context));
                continue;
            }

            var typeString = data.Value.Tag[6..];

            if (!reflection.TryLooseGetType(typeString, out var type))
            {
                validated.Add(new ErrorNode(node.GetKeyNode(key), $"Unable to find type for {typeString}"));
                continue;
            }

            var validatedNode = serializationManager.ValidateNode(type, data.Value, context);
            validated.Add(validatedNode);
        }

        return new ValidatedSequenceNode(validated);
    }

    public NPCBlackboard Read(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx, ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<NPCBlackboard>? instanceProvider = null)
    {
        var value = instanceProvider != null ? instanceProvider() : new NPCBlackboard();

        if (node.Count <= 0)
            return value;

        var reflection = dependencies.Resolve<IReflectionManager>();

        foreach (var data in node)
        {
            // Normalize key for resilient matching
            var key = data.Key?.Trim() ?? string.Empty;

            if (data.Value.Tag == null)
            {
                // Support plain booleans for common navigation toggles.
                if (SimpleBoolKeys.Contains(key))
                {
                    var bbBool = serializationManager.Read<bool>(data.Value, hookCtx, context);
                    value.SetValue(key, bbBool);
                    continue;
                }

                // Attempt to gracefully coerce common primitive scalar types.
                if (data.Value is Robust.Shared.Serialization.Markdown.Value.ValueDataNode vNode)
                {
                    var text = vNode.Value;
                    if (!string.IsNullOrEmpty(text))
                    {
                        // Try boolean first
                        if (bool.TryParse(text, out var b))
                        {
                            value.SetValue(key, b);
                            continue;
                        }

                        // Then numeric types
                        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        {
                            value.SetValue(key, i);
                            continue;
                        }

                        if (float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var f))
                        {
                            value.SetValue(key, f);
                            continue;
                        }

                        // Fallback to string
                        value.SetValue(key, text);
                        continue;
                    }
                }

                // Final fallback: read as string via serializer (may return empty string)
                var bbString = serializationManager.Read<string?>(data.Value, hookCtx, context) ?? string.Empty;
                value.SetValue(key, bbString);
                continue;
            }

            var typeString = data.Value.Tag[6..];

            if (!reflection.TryLooseGetType(typeString, out var type))
                throw new NullReferenceException($"Found null type for {key}");

            var bbData = serializationManager.Read(type, data.Value, hookCtx, context);

            if (bbData == null)
                throw new NullReferenceException($"Found null data for {key}, expected {type}");

            value.SetValue(key, bbData);
        }

        return value;
    }

    public void CopyTo(
        ISerializationManager serializationManager,
        NPCBlackboard source,
        ref NPCBlackboard target,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        target.Clear();
        using var enumerator = source.GetEnumerator();

        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            target.SetValue(current.Key, current.Value);
        }
    }
}
