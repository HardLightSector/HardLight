using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Prototypes;

public readonly record struct EntProtoComp<T>(EntityPrototype Prototype, T Component) where T : IComponent
{
    public static implicit operator EntProtoComp<T>((EntityPrototype Prototype, T Component) tuple)
    {
        return new EntProtoComp<T>(tuple.Prototype, tuple.Component);
    }
}
