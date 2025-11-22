using System.Diagnostics.CodeAnalysis;
using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Prototypes;

public sealed class ALPrototypeSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public IEnumerable<(EntityPrototype Prototype, T Component)> EnumerateComponents<T>() where T : IComponent, new()
    {
        foreach (var entity in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (entity.TryGetComponent(out T? comp, _compFactory))
                yield return (entity, comp);
        }
    }

    public IEnumerable<EntityPrototype> EnumerateEntities<T>() where T : IComponent, new()
    {
        foreach (var entity in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (entity.HasComponent<T>(_compFactory))
                yield return entity;
        }
    }

    public EntityPrototype? IndexOrNull(EntProtoId id)
    {
        return _prototype.TryIndex(id, out var proto) ? proto : null;
    }

    public bool TryIndexComponent<T>(EntProtoId id,
        [NotNullWhen(true)] out EntityPrototype? entity,
        [NotNullWhen(true)] out T? comp) where T : IComponent, new()
    {
        entity = default;
        comp = default;
        return _prototype.TryIndex(id, out var proto) &&
               proto.TryGetComponent(out comp, _compFactory);
    }

    public bool TryIndexComponent<T>(EntProtoId id,
        [NotNullWhen(true)] out T? comp) where T : IComponent, new()
    {
        return TryIndexComponent(id, out _, out comp);
    }

    public T? IndexOrNullComponent<T>(EntProtoId id) where T : IComponent, new()
    {
        if (!_prototype.TryIndex(id, out var proto) ||
            !proto.TryGetComponent(out T? comp, _compFactory))
        {
            return default;
        }

        return comp;
    }
}
