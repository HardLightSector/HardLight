using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Shuttles.Save;
using Content.Shared._NF.Shipyard.Components;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.Log;

namespace Content.Server.Shuttles.Save
{
    public sealed class ShipSaveSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

        // Static caches for admin ship save interactions
        private static readonly Dictionary<string, Action<string>> PendingAdminRequests = new();
        private static readonly Dictionary<string, List<(string filename, string shipName, DateTime timestamp)>> PlayerShipCache = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<RequestSaveShipServerMessage>(OnRequestSaveShipServer);
            SubscribeNetworkEvent<RequestLoadShipMessage>(OnRequestLoadShip);
            SubscribeNetworkEvent<RequestAvailableShipsMessage>(OnRequestAvailableShips);
            SubscribeNetworkEvent<AdminSendPlayerShipsMessage>(OnAdminSendPlayerShips);
            SubscribeNetworkEvent<AdminSendShipDataMessage>(OnAdminSendShipData);
        }

        private void OnRequestSaveShipServer(RequestSaveShipServerMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            var deedUid = new EntityUid((int)msg.DeedUid);
            // Only save the grid referenced by the shuttle deed. Do NOT fall back to the player's current grid / station.
            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deed) || deed.ShuttleUid == null ||
                !_entityManager.TryGetEntity(deed.ShuttleUid.Value, out var shuttleNetUid))
            {
                Logger.Warning($"Player {playerSession.Name} attempted ship save without a valid shuttle deed / shuttle reference on ID {deedUid}");
                return;
            }

            var gridToSave = shuttleNetUid.Value;
            if (!_entityManager.HasComponent<MapGridComponent>(gridToSave))
            {
                Logger.Warning($"Player {playerSession.Name} deed shuttle {gridToSave} is not a grid");
                return;
            }

            var shipName = deed.ShuttleName ?? $"SavedShip_{DateTime.Now:yyyyMMdd_HHmmss}";

            var shipyardGridSaveSystem = _entitySystemManager.GetEntitySystem<Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem>();
            Logger.Info($"Player {playerSession.Name} is saving deed-referenced ship {shipName} (grid {gridToSave})");
            var success = shipyardGridSaveSystem.TrySaveGridAsShip(gridToSave, shipName, playerSession.UserId.ToString(), playerSession);
            if (success)
            {
                Logger.Info($"Successfully saved ship {shipName}");
                // Mirror ShipyardGridSaveSystem deed/grid cleanup to avoid stale ownership
                if (_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deedComp))
                {
                    _entityManager.RemoveComponent<ShuttleDeedComponent>(deedUid);
                }

                // Remove any other deeds that referenced this shuttle
                var toRemove = new List<EntityUid>();
                var query = _entityManager.EntityQueryEnumerator<ShuttleDeedComponent>();
                while (query.MoveNext(out var ent, out var deedRef))
                {
                    if (deedRef.ShuttleUid != null && _entityManager.TryGetEntity(deedRef.ShuttleUid.Value, out var entUid) && entUid == gridToSave)
                        toRemove.Add(ent);
                }
                foreach (var uidToClear in toRemove)
                {
                    _entityManager.RemoveComponent<ShuttleDeedComponent>(uidToClear);
                }

                // Delete the live grid after save to reset ownership chain
                _entityManager.QueueDeleteEntity(gridToSave);
            }
            else
            {
                Logger.Error($"Failed to save ship {shipName}");
            }
        }
        public void RequestSaveShip(EntityUid deedUid, ICommonSession? playerSession)
        {
            if (playerSession == null)
            {
                Logger.Warning($"Attempted to save ship for deed {deedUid} without a valid player session.");
                return;
            }

            if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(deedUid, out var deedComponent))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with invalid deed UID: {deedUid}");
                return;
            }

            if (deedComponent.ShuttleUid == null || !_entityManager.TryGetEntity(deedComponent.ShuttleUid.Value, out var shuttleUid) || !_entityManager.TryGetComponent<MapGridComponent>(shuttleUid.Value, out var grid))
            {
                Logger.Warning($"Player {playerSession.Name} tried to save ship with deed {deedUid} but no valid shuttle UID found.");
                return;
            }

            // Integrate with ShipyardGridSaveSystem for ship saving functionality
            var shipName = deedComponent.ShuttleName ?? "SavedShip_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get the ShipyardGridSaveSystem and use it to save the ship
            var shipyardGridSaveSystem = _entitySystemManager.GetEntitySystem<Content.Server._NF.Shipyard.Systems.ShipyardGridSaveSystem>();

            Logger.Info($"Player {playerSession.Name} is saving ship {shipName} via ShipyardGridSaveSystem");

            // Save the ship using the working grid-based system (synchronously on main thread)
            var success2 = shipyardGridSaveSystem.TrySaveGridAsShip(shuttleUid.Value, shipName, playerSession.UserId.ToString(), playerSession);
            if (success2)
            {
                // Clean up the deed after successful save
                _entityManager.RemoveComponent<ShuttleDeedComponent>(deedUid);
                Logger.Info($"Successfully saved and removed ship {shipName}");
            }
            else
            {
                Logger.Error($"Failed to save ship {shipName}");
            }
        }

        private void OnRequestLoadShip(RequestLoadShipMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            Logger.Info($"Player {playerSession.Name} requested to load ship from YAML data");

            // TODO: Implement ship loading from saved files
            // This would involve deserializing the ship data and spawning it in the game world
            // For now, we just log the request
        }

        private void OnRequestAvailableShips(RequestAvailableShipsMessage msg, EntitySessionEventArgs args)
        {
            var playerSession = args.SenderSession;
            if (playerSession == null)
                return;

            // Client handles available ships from local user data
            Logger.Info($"Player {playerSession.Name} requested available ships - client handles this locally");
        }

        private void OnAdminSendPlayerShips(AdminSendPlayerShipsMessage msg, EntitySessionEventArgs args)
        {
            var key = $"player_ships_{msg.AdminName}";
            if (PendingAdminRequests.TryGetValue(key, out var callback))
            {
                // Cache the ship data for later commands
                PlayerShipCache[key] = msg.Ships;

                var result = $"=== Ships for player ===\n\n";
                for (int i = 0; i < msg.Ships.Count; i++)
                {
                    var (filename, shipName, timestamp) = msg.Ships[i];
                    result += $"[{i + 1}] {shipName} ({filename})\n";
                    result += $"    Saved: {timestamp:yyyy-MM-dd HH:mm:ss}\n";
                    result += "\n";
                }
                callback(result);
                PendingAdminRequests.Remove(key);
            }
        }

        private void OnAdminSendShipData(AdminSendShipDataMessage msg, EntitySessionEventArgs args)
        {
            var key = $"ship_data_{msg.AdminName}_{msg.ShipFilename}";
            if (PendingAdminRequests.TryGetValue(key, out var callback))
            {
                callback(msg.ShipData);
                PendingAdminRequests.Remove(key);
            }
        }

        public static void RegisterAdminRequest(string key, Action<string> callback)
        {
            PendingAdminRequests[key] = callback;
        }

        public void SendAdminRequestPlayerShips(Guid playerId, string adminName, ICommonSession targetSession)
        {
            RaiseNetworkEvent(new AdminRequestPlayerShipsMessage(playerId, adminName), targetSession);
        }

        public void SendAdminRequestShipData(string filename, string adminName, ICommonSession targetSession)
        {
            RaiseNetworkEvent(new AdminRequestShipDataMessage(filename, adminName), targetSession);
        }
    }
}
