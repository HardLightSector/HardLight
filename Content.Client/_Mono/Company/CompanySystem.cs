using Content.Shared._Mono.Company;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;
using Robust.Shared.Localization;

namespace Content.Client._Mono.Company;

/// <summary>
/// Client-side system for displaying company information in examine text.
/// </summary>
public sealed class CompanySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Shared._Mono.Company.CompanyComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, Shared._Mono.Company.CompanyComponent component, ExaminedEvent args)
    {
        // Try to get the prototype for the company
        if (_prototypeManager.TryIndex<CompanyPrototype>(component.CompanyName, out var prototype) && component.CompanyName != "None")
        {
            // Use the color from the prototype with gender-appropriate pronoun
            args.PushMarkup(Loc.GetString("examine-company", 
                ("entity", uid), 
                ("company", $"[color={prototype.Color.ToHex()}]{prototype.Name}[/color]")), 
                priority: 100); // Much higher priority (100) will ensure it's at the top
        }
        else if (component.CompanyName != "None")
        {
            // Fallback for companies without prototypes
            args.PushMarkup(Loc.GetString("examine-company", 
                ("entity", uid), 
                ("company", $"[color=yellow]{component.CompanyName}[/color]")), 
                priority: 100);
        }
        // Don't show anything for "None" company
    }
}
