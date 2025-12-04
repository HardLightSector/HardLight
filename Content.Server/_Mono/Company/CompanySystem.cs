using Content.Shared._Mono.Company;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

/// <summary>
/// This system handles assigning a company to players when they join.
/// </summary>
public sealed class CompanySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;

    // Dictionary to store original company preferences for players
    private readonly Dictionary<string, string> _playerOriginalCompanies = new();

    private readonly HashSet<string> _ngcJobs = new()
    {
        "Sheriff",
        "StationRepresentative",
        "StationTrafficController",
        "Bailiff",
        "SeniorOfficer", // Sergeant
        "Deputy",
        "Brigmedic",
        "NFDetective",
        "PublicAffairsLiaison",
        "DirectorOfCare"
    };

    private readonly HashSet<string> _rogueJobs = new()
    {
        "PirateCaptain",
        "PirateFirstMate"
    };

    private readonly HashSet<string> _usspJobs = new()
    {
        "USSPCommissar",
        "USSPSergeant",
        "USSPCorporal",
        "USSPMedic",
        "USSPRifleman"
    };

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to player spawn event to add the company component
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);

        // Subscribe to examination to show the company on examine
        SubscribeLocalEvent<Shared._Mono.Company.CompanyComponent, ExaminedEvent>(OnExamined);

        // Subscribe to player detached event to clean up stored preferences
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        // Clean up stored preferences when player disconnects
        _playerOriginalCompanies.Remove(args.Player.UserId.ToString());
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Add the company component with the player's saved company
        var companyComp = EnsureComp<Shared._Mono.Company.CompanyComponent>(args.Mob);

        var playerId = args.Player.UserId.ToString();
        var profileCompany = args.Profile.Company;

        // Store the player's original company preference if not already stored
        if (!_playerOriginalCompanies.ContainsKey(playerId))
        {
            _playerOriginalCompanies[playerId] = profileCompany;
        }

        // Check if player's job is one of the TSF jobs
        if (args.JobId != null && _ngcJobs.Contains(args.JobId))
        {
            // Assign TSF company
            companyComp.CompanyName = "TSF";
        }
        // Check if player's job is one of the Rogue jobs
        else if (args.JobId != null && _rogueJobs.Contains(args.JobId))
        {
            // Assign Rogue company
            companyComp.CompanyName = "Rogue";
        }
        // Check if player's job is one of the USSP jobs
        else if (args.JobId != null && _usspJobs.Contains(args.JobId))
        {
            // Assign USSP company
            companyComp.CompanyName = "USSP";
        }
        else
        {
            // Only consider whitelist if the player has NO specific company preference
            bool loginFound = false;

            // Only check logins if the player hasn't explicitly set a company preference
            // or if their preference is "None"
            if (string.IsNullOrEmpty(profileCompany))
            {
                // Check for company login whitelists
                foreach (var companyProto in _prototypeManager.EnumeratePrototypes<CompanyPrototype>())
                {
                    if (companyProto.Logins.Contains(args.Player.Name))
                    {
                        companyComp.CompanyName = companyProto.ID;
                        loginFound = true;
                        break;
                    }
                }
            }

            // If no login was found or login check was skipped due to player preference, use the player's preference
            if (!loginFound)
            {
                // Use "None" as fallback for empty company
                if (string.IsNullOrEmpty(profileCompany))
                    profileCompany = "None";

                // Restore the player's original company preference
                companyComp.CompanyName = profileCompany;
            }
        }

        // Ensure the component is networked to clients
        Dirty(args.Mob, companyComp);
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
