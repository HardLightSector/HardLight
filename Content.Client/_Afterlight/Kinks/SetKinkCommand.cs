using Content.Shared.Database._Afterlight;
using Robust.Shared.Console;

namespace Content.Client._Afterlight.Kinks;

#if !FULL_RELEASE
public sealed class SetKinkCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entity = default!;

    public override string Command => "setkink";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = _entity.System<KinkSystem>();
        system.ClientSetPreference(args[0], Enum.Parse<KinkPreference>(args[1]));
    }
}
#endif
