using Robust.Shared.GameStates;

namespace Content.Shared._DV.Chemistry.Components;

/// <summary>
/// Prevents injections being used on this entity.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BlockInjectionComponent : Component
{
    /// <summary>
    /// If true, this component will block injections from syringes.
    /// </summary>
    [DataField]
    public bool BlockSyringe = true;

    /// <summary>
    /// If true, this component will block injections from hypospray.
    /// </summary>
    [DataField]
    public bool BlockHypospray;

    /// <summary>
    /// If true, this component will block injections from projectile.
    /// </summary>
    [DataField]
    public bool BlockInjectOnProjectile;

    /// <summary>
    /// Reason why injections are blocked. Used for localization keys like "injector-component-deny-{BlockReason}".
    /// </summary>
    [DataField]
    public string BlockReason { get; set; } = string.Empty;
}
