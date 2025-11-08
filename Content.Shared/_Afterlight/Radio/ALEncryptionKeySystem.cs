using System.Linq;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Radio;

/// <summary>
/// This handles...
/// </summary>
public sealed class ALEncryptionKeySystem : EntitySystem
{
    /// <inheritdoc/>
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<EncryptionKeyComponent,MapInitEvent>(OnMapInit);

        void OnMapInit(Entity<EncryptionKeyComponent> ent, ref MapInitEvent args)
        {
            var comp = ent.Comp;
            if (comp.AllComms)
                comp.Channels = [.. _protoManager.EnumeratePrototypes<RadioChannelPrototype>().Select(x => x.ID)];
        }
    }
}