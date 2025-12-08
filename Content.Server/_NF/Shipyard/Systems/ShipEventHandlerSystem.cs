using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard;
using Content.Server._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.BUI;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Server.GameObjects;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Handles events related to ship operations (saving and loading)
/// Manages console updates and post-operation cleanup
/// </summary>
public sealed class ShipEventHandlerSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("shipyard.events");

        // Subscribe to ship events
        SubscribeLocalEvent<ShipSavedEvent>(OnShipSaved);
        SubscribeLocalEvent<ShipLoadedEvent>(OnShipLoaded);

        _sawmill.Info("ShipEventHandlerSystem initialized");
    }

    /// <summary>
    /// Handles ShipSavedEvent - updates consoles and performs post-save operations
    /// </summary>
    private void OnShipSaved(ShipSavedEvent args)
    {
        try
        {
            _sawmill.Info($"Handling ShipSavedEvent for ship '{args.ShipName}' by player {args.PlayerUserId}");

            // For ship saves, we could update specific console if we had the console ID
            // For now, we just log the successful save
            // TODO: Consider passing console information in save events if needed

            // Log the ship save operation
            _sawmill.Info($"Ship '{args.ShipName}' was successfully saved by player {args.PlayerUserId}");

            // Additional post-save operations could be added here
            // For example: logging to database, notifications to other players, etc.
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error handling ShipSavedEvent for ship '{args.ShipName}': {ex}");
        }
    }

    /// <summary>
    /// Handles ShipLoadedEvent - updates consoles and performs post-load operations
    /// </summary>
    private void OnShipLoaded(ShipLoadedEvent args)
    {
        try
        {
            _sawmill.Info($"Handling ShipLoadedEvent for ship '{args.ShipName}' loaded by player {args.PlayerUserId}");

            // Update the specific console that was used for loading
            UpdateSpecificConsole(args.ConsoleUid, args.ShipyardChannel);

            // Log the ship load operation
            _sawmill.Info($"Ship '{args.ShipName}' was successfully loaded by player {args.PlayerUserId} on grid {args.ShipGridUid}");

            // Additional post-load operations could be added here
            // For example: announcing the ship arrival, updating station records, etc.
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error handling ShipLoadedEvent for ship '{args.ShipName}': {ex}");
        }
    }

    /// <summary>
    /// Updates a specific shipyard console's UI
    /// </summary>
    private void UpdateSpecificConsole(EntityUid consoleUid, string shipyardChannel)
    {
        try
        {
            // Create a basic state to refresh the UI with default values
            var availableShips = new List<string>();
            var unavailableShips = new List<string>();
            var shipyardPrototypes = (availableShips, unavailableShips);

            var state = new ShipyardConsoleInterfaceState(
                0,                    // int balance
                true,                 // bool accessGranted
                null,                 // string? shipDeedTitle
                0,                    // int shipSellValue
                false,                // bool isTargetIdPresent
                0,                    // byte uiKey
                shipyardPrototypes,   // tuple shipyardPrototypes
                shipyardChannel,      // string shipyardName
                false,                // bool freeListings
                0.6f                  // float sellRate
            );

            _userInterface.SetUiState(consoleUid, ShipyardConsoleUiKey.Shipyard, state);

            _sawmill.Debug($"Updated console {consoleUid}");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error updating console {consoleUid}: {ex}");
        }
    }
}
