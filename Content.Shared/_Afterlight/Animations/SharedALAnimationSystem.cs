using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Animations;

public abstract class SharedALAnimationSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;

    /// <summary>
    ///     Plays an animation with a specific key on an entity.
    /// </summary>
    /// <param name="ent">The entity to play the animation on.</param>
    /// <param name="key">
    ///     The ID of the animation stored in <see cref="ALAnimationComponent.Animations"/>
    /// </param>
    public void Play(Entity<ALAnimationComponent?> ent, string key)
    {
        if (_net.IsClient) // TODO afterlight
            return;

        if (!TryGetNetEntity(ent, out var netEnt))
            return;

        var ev = new ALPlayAnimationEvent(netEnt.Value, new ALAnimationId(key));
        var filter = Filter.Pvs(ent);
        RaiseNetworkEvent(ev, filter);
    }

    /// <summary>
    ///     Plays an animation on an entity once.
    ///     The duration of the animation will be equal to one loop of the state on its RSI.
    /// </summary>
    /// <param name="ent">The entity to play the animation on.</param>
    /// <param name="animationRsi">The RSI state to use for playing the animation.</param>
    /// <param name="defaultRsi">The RSI state to set when the animation ends.</param>
    /// <param name="layer">
    ///     Which layer to play the animation on. If null, it will choose the first
    ///     layer by default.
    /// </param>
    public void Flick(Entity<ALAnimationComponent?> ent,
        SpriteSpecifier.Rsi animationRsi,
        SpriteSpecifier.Rsi defaultRsi,
        string? layer = null)
    {
        if (_net.IsClient) // TODO afterlight
            return;

        if (!TryGetNetEntity(ent, out var netEnt))
            return;

        var ev = new ALFlickEvent(netEnt.Value, animationRsi, defaultRsi, layer);
        var filter = Filter.Pvs(ent);
        RaiseNetworkEvent(ev, filter);
    }
}
