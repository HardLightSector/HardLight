using System.Threading;
using AngleSharp;
using AngleSharp.Dom;
using Content.Shared._Afterlight.Kinks;
using Content.Shared.Database._Afterlight;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Afterlight.Kinks;

public sealed class KinkSystem : SharedKinkSystem
{
    [Dependency] private readonly KinkManager _manager = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    private IBrowsingContext _browsingContext = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<UpdateSingleKinkClientEvent>(OnNetworkUpdateSingleKink);
        SubscribeNetworkEvent<UpdateKinksClientEvent>(OnNetworkUpdateKinks);
        SubscribeNetworkEvent<UpdateKinksSinglePreferenceClientEvent>(OnNetworkUpdateKinkSinglePreference);

        SubscribeNetworkEvent<KinkImportFlistClientEvent>(OnNetworkImportFlist);

        _manager.OnKinksUpdated += OnManagerKinksUpdated;
        _manager.ReloadPrototypes();

        var configuration = Configuration.Default.WithTemporaryCookies().WithDefaultLoader();
        _browsingContext = BrowsingContext.New(configuration);
    }

    private void OnNetworkUpdateSingleKink(UpdateSingleKinkClientEvent msg, EntitySessionEventArgs args)
    {
        _manager.SetKink(args.SenderSession.UserId, msg.Kink, msg.Preference);
    }

    private void OnNetworkUpdateKinks(UpdateKinksClientEvent msg, EntitySessionEventArgs args)
    {
        // TODO AFTERLIGHT immutable dictionary when https://github.com/space-wizards/netserializer/pull/5 is merged
        _manager.UpdateKinks(args.SenderSession.UserId, msg.Kinks);
    }

    private void OnNetworkUpdateKinkSinglePreference(UpdateKinksSinglePreferenceClientEvent msg, EntitySessionEventArgs args)
    {
        _manager.UpdateKinks(args.SenderSession.UserId, msg.Kinks, msg.Preference);
    }

    private async void OnNetworkImportFlist(KinkImportFlistClientEvent msg, EntitySessionEventArgs args)
    {
        ImportFlist(msg.Link, args.SenderSession);
        var serverEv = new KinkImportedFlistServerEvent();
        RaiseNetworkEvent(serverEv, args.SenderSession);
    }

    private void OnManagerKinksUpdated(KinksUpdatedEvent ev)
    {
        if (_player.TryGetSessionById(ev.Player, out var session))
        {
            if (session.AttachedEntity is { } ent)
            {
                UpdateKinks(session, ent);
                RaiseLocalEvent(ent, ev);
            }

            RaiseNetworkEvent(ev, session);
        }

        RaiseLocalEvent(ev);
    }

    protected override void OnNetworkKinksUpdated(KinksUpdatedEvent msg, EntitySessionEventArgs args)
    {
        _manager.UpdateKinks(args.SenderSession.UserId, msg.Kinks);
    }

    protected override Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>? GetKinks(NetUserId player)
    {
        return _manager.GetKinks(player);
    }

    private async void ImportFlist(string link, ICommonSession player)
    {
        try
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var address))
                return;

            if (address.Host != "www.f-list.net")
                return;

            var kinks = new Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>();
            var document = await _browsingContext.OpenAsync(address.ToString(), CancellationToken.None);

            // Check if we hit the warning page
            // Yes this is stupid, but what's even stupider is that just reloading the page works
            if (document.Title == "F-list - Warning")
                document = await _browsingContext.OpenAsync(address.ToString(), CancellationToken.None);

            ImportFlistSelector(document, kinks, "#Character_FetishlistFave", KinkPreference.Favourite);
            ImportFlistSelector(document, kinks, "#Character_FetishlistYes", KinkPreference.Yes);
            ImportFlistSelector(document, kinks, "#Character_FetishlistMaybe", KinkPreference.Ask);
            ImportFlistSelector(document, kinks, "#Character_FetishlistNo", KinkPreference.No);

            await _manager.UpdateKinksAsync(player.UserId, kinks);
        }
        catch (Exception e)
        {
            Log.Error($"Error importing f-list {link} for player {player.Name}:\n{e}");
        }
    }

    private void ImportFlistSelector(IDocument document, Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks, string selector, KinkPreference preference)
    {
        if (document.QuerySelector(selector) is not { } column)
            return;

        var kinkElements = column.QuerySelectorAll("a");
        foreach (var kinkElement in kinkElements)
        {
            var kinkName = kinkElement.TextContent.Trim();
            if (!FlistImports.TryGetValue(kinkName, out var kink))
                continue;

            kinks[kink.ID] = preference;
        }
    }
}
