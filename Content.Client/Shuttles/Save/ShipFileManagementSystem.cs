using Content.Shared.Shuttles.Save;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Log;
using Robust.Shared.ContentPack;

namespace Content.Client.Shuttles.Save
{
    public sealed class ShipFileManagementSystem : EntitySystem
    {
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IResourceManager _resourceManager = default!;

        // Static data shared across all instances to handle multiple system instances
        private static readonly Dictionary<string, string> _staticCachedShipData = new();
        private static readonly Dictionary<string, (string shipName, DateTime timestamp)> _staticShipMetadataCache = new();
        private static readonly List<string> _staticAvailableShips = new();
        private static event Action? _staticOnShipsUpdated;
        private static event Action<string>? _staticOnShipLoaded;
        private static bool _staticIndexUpdateNeeded = false;
        private static DateTime _lastIndexUpdate = DateTime.MinValue;
        private static readonly TimeSpan IndexUpdateCooldown = TimeSpan.FromSeconds(1);

        public event Action? OnShipsUpdated
        {
            add => _staticOnShipsUpdated += value;
            remove => _staticOnShipsUpdated -= value;
        }

        public event Action<string>? OnShipLoaded
        {
            add => _staticOnShipLoaded += value;
            remove => _staticOnShipLoaded -= value;
        }

        private static int _instanceCounter = 0;
        private readonly int _instanceId;

        public ShipFileManagementSystem()
        {
            _instanceId = ++_instanceCounter;
            // Reduced logging for performance
        }

        public override void Initialize()
        {
            // Reduced logging - only log if first instance or errors
            base.Initialize();
            SubscribeNetworkEvent<SendShipSaveDataClientMessage>(HandleSaveShipDataClient);
            SubscribeNetworkEvent<SendAvailableShipsMessage>(HandleAvailableShipsMessage);
            SubscribeNetworkEvent<ShipConvertedToSecureFormatMessage>(HandleShipConvertedToSecureFormat);
            SubscribeNetworkEvent<AdminRequestPlayerShipsMessage>(HandleAdminRequestPlayerShips);
            SubscribeNetworkEvent<AdminRequestShipDataMessage>(HandleAdminRequestShipData);

            // Ensure saved_ships directory exists on startup
            EnsureSavedShipsDirectoryExists();

            // Only load existing ships if we haven't already loaded them
            if (_staticAvailableShips.Count == 0)
            {
                // Load existing saved ships from user data
                LoadExistingShips();
            }
            // Skip reload if ships already loaded by previous instance

            // Request available ships from server
            RaiseNetworkEvent(new RequestAvailableShipsMessage());
        }

        private void EnsureSavedShipsDirectoryExists()
        {
            // Exports folder already exists, no need to create directories
        }

        private void HandleSaveShipDataClient(SendShipSaveDataClientMessage message)
        {
            // Save ship data to user data directory using sandbox-safe resource manager
            Logger.Info($"Client received ship save data for: {message.ShipName}");

            // Ensure directory exists before saving
            EnsureSavedShipsDirectoryExists();

            var fileName = $"/Exports/{message.ShipName}_{DateTime.Now:yyyyMMdd_HHmmss}.yml";

            try
            {
                using var writer = _resourceManager.UserData.OpenWriteText(new(fileName));
                writer.Write(message.ShipData);
                Logger.Info($"Saved ship {message.ShipName} to user data: {fileName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save ship {message.ShipName}: {ex.Message}");
            }

            // Cache the data and update available ships list
            _staticCachedShipData[fileName] = message.ShipData;
            if (!_staticAvailableShips.Contains(fileName))
            {
                _staticAvailableShips.Add(fileName);
            }

            // Mark index update as needed but don't update immediately
            _staticIndexUpdateNeeded = true;

            // Trigger UI update
            _staticOnShipsUpdated?.Invoke();
        }

        private void HandleAvailableShipsMessage(SendAvailableShipsMessage message)
        {
            // Don't clear locally loaded ships - server message is for server-side ships only
            // The client handles local ship files independently
            Logger.Debug($"Instance #{_instanceId}: Received {message.ShipNames.Count} available ships from server (not clearing local ships)");
            Logger.Debug($"Instance #{_instanceId}: Current state before processing: {_staticAvailableShips.Count} ships, {_staticCachedShipData.Count} cached");

            // Only add server ships that aren't already in our local list
            foreach (var serverShip in message.ShipNames)
            {
                if (!_staticAvailableShips.Contains(serverShip))
                {
                    _staticAvailableShips.Add(serverShip);
                    Logger.Debug($"Instance #{_instanceId}: Added server ship: {serverShip}");
                }
            }

            Logger.Info($"Instance #{_instanceId}: Final state after processing: {_staticAvailableShips.Count} ships");
        }

        private void HandleShipConvertedToSecureFormat(ShipConvertedToSecureFormatMessage message)
        {
            Logger.Warning($"Legacy ship '{message.ShipName}' was automatically converted to secure format by server");

            // Find and overwrite the original file with the converted version
            var originalFile = _staticAvailableShips.FirstOrDefault(ship =>
                ship.Contains(message.ShipName) || _staticCachedShipData.ContainsKey(ship) &&
                _staticCachedShipData[ship].Contains($"shipName: {message.ShipName}"));

            if (originalFile != null)
            {
                try
                {
                    // Overwrite the original file with converted data
                    using var writer = _resourceManager.UserData.OpenWriteText(new(originalFile));
                    writer.Write(message.ConvertedYamlData);

                    // Update cached data
                    _staticCachedShipData[originalFile] = message.ConvertedYamlData;

                    Logger.Info($"Successfully overwrote legacy ship file '{originalFile}' with secure format");
                    Logger.Info($"Ship '{message.ShipName}' is now protected against tampering");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to overwrite legacy ship file '{originalFile}': {ex.Message}");
                    Logger.Warning($"Legacy ship '{message.ShipName}' conversion failed - please manually re-save the ship to get secure format");
                }
            }
            else
            {
                Logger.Warning($"Could not find original file for converted ship '{message.ShipName}' - creating new file");

                // Create a new file with the converted data
                var fileName = $"/Exports/{message.ShipName}_converted_{DateTime.Now:yyyyMMdd_HHmmss}.yml";
                try
                {
                    using var writer = _resourceManager.UserData.OpenWriteText(new(fileName));
                    writer.Write(message.ConvertedYamlData);

                    // Add to cache and available ships
                    _staticCachedShipData[fileName] = message.ConvertedYamlData;
                    if (!_staticAvailableShips.Contains(fileName))
                    {
                        _staticAvailableShips.Add(fileName);
                    }

                    Logger.Info($"Created new secure format file for converted ship: {fileName}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to create converted ship file: {ex.Message}");
                }
            }
        }

        public void RequestSaveShip(EntityUid deedUid)
        {
            RaiseNetworkEvent(new RequestSaveShipServerMessage((uint)deedUid.Id));
        }

        public async Task LoadShipFromFile(string filePath)
        {
            var yamlData = await GetShipYamlData(filePath);
            if (yamlData != null)
            {
                RaiseNetworkEvent(new RequestLoadShipMessage(yamlData));

                // Extract ship name for the event (extract filename without path and extension)
                var shipName = ExtractFileNameWithoutExtension(filePath);
                _staticOnShipLoaded?.Invoke(shipName);
            }
            await Task.CompletedTask;
        }

        public async Task<string?> GetShipYamlData(string filePath)
        {
            string? yamlData;

            // Check cache first, load from disk if needed (lazy loading)
            if (_staticCachedShipData.TryGetValue(filePath, out yamlData))
            {
                // Data already cached
            }
            else
            {
                // Load from disk
                try
                {
                    using var reader = _resourceManager.UserData.OpenText(new(filePath));
                    yamlData = reader.ReadToEnd();
                    _staticCachedShipData[filePath] = yamlData;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to load ship data from {filePath}: {ex.Message}");
                    return null;
                }
            }

            await Task.CompletedTask;
            return yamlData;
        }

        private void LoadExistingShips()
        {
            try
            {
                Logger.Info($"Instance #{_instanceId}: Attempting to find saved ship files...");

                // Try UserData.Find to enumerate all .yml files
                var (ymlFiles, directories) = _resourceManager.UserData.Find("*.yml", recursive: true);

                var ymlFilesList = ymlFiles.ToList();
                Logger.Info($"Instance #{_instanceId}: Found {ymlFilesList.Count.ToString()} .yml files total");

                foreach (var file in ymlFiles)
                {
                    var filePath = file.ToString();

                    // Accept any .yml file in Exports (not just ship_index)
                    if (filePath.Contains("Exports") && filePath.EndsWith(".yml") && !filePath.Contains("ship_index"))
                    {
                        if (!_staticAvailableShips.Contains(filePath))
                        {
                            _staticAvailableShips.Add(filePath);

                            // Use lazy loading - only cache metadata for now
                            try
                            {
                                CacheShipMetadata(filePath);
                            }
                            catch (Exception shipEx)
                            {
                                Logger.Error($"Failed to cache metadata for {filePath}: {shipEx.Message}");
                            }
                        }
                    }
                }

                Logger.Debug($"Instance #{_instanceId}: Final result: Loaded {_staticAvailableShips.Count} saved ships from Exports directory");

                // Trigger UI update
                _staticOnShipsUpdated?.Invoke();
            }
            catch (NotImplementedException)
            {
                // In test environments, the Find method may not be implemented
                // This is expected and should not cause test failures
                Logger.Debug($"Instance #{_instanceId}: Ship file enumeration not available in test environment");
            }
            catch (Exception ex)
            {
                Logger.Error($"Instance #{_instanceId}: Failed to load existing ships: {ex.Message}");
            }
        }

        private void LoadShipIndex()
        {
            try
            {
                if (_resourceManager.UserData.Exists(new("/Exports/ship_index.txt")))
                {
                    using var reader = _resourceManager.UserData.OpenText(new("/Exports/ship_index.txt"));
                    var content = reader.ReadToEnd();
                    var shipFiles = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var shipFile in shipFiles)
                    {
                        if (!string.IsNullOrWhiteSpace(shipFile) && !_staticAvailableShips.Contains(shipFile))
                        {
                            _staticAvailableShips.Add(shipFile);

                            // Load the ship data into cache
                            try
                            {
                                if (_resourceManager.UserData.Exists(new(shipFile)))
                                {
                                    using var shipReader = _resourceManager.UserData.OpenText(new(shipFile));
                                    var shipData = shipReader.ReadToEnd();
                                    _staticCachedShipData[shipFile] = shipData;
                                }
                            }
                            catch (Exception shipEx)
                            {
                                Logger.Error($"Failed to load ship data for {shipFile}: {shipEx.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load ship index: {ex.Message}");
            }
        }

        private void UpdateShipIndex()
        {
            try
            {
                // Rate limit index updates
                var now = DateTime.Now;
                if (!_staticIndexUpdateNeeded || (now - _lastIndexUpdate) < IndexUpdateCooldown)
                    return;

                var indexContent = string.Join('\n', _staticAvailableShips);
                using var writer = _resourceManager.UserData.OpenWriteText(new("/Exports/ship_index.txt"));
                writer.Write(indexContent);

                _staticIndexUpdateNeeded = false;
                _lastIndexUpdate = now;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update ship index: {ex.Message}");
            }
        }

        private void CacheShipMetadata(string filePath)
        {
            try
            {
                using var reader = _resourceManager.UserData.OpenText(new(filePath));
                var content = reader.ReadToEnd();

                // Parse metadata without caching full content (lazy loading)
                var lines = content.Split('\n');
                var shipName = lines.FirstOrDefault(l => l.Trim().StartsWith("shipName:"))?.Split(':')[1].Trim() ?? "Unknown";
                var timestampStr = lines.FirstOrDefault(l => l.Trim().StartsWith("timestamp:"))?.Split(':', 2)[1].Trim() ?? "";

                if (DateTime.TryParse(timestampStr, out var timestamp))
                {
                    _staticShipMetadataCache[filePath] = (shipName, timestamp);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to cache metadata for {filePath}: {ex.Message}");
            }
        }

        // Update ship index periodically instead of on every change
        public void FlushPendingIndexUpdates()
        {
            if (_staticIndexUpdateNeeded)
            {
                UpdateShipIndex();
            }
        }

        public List<string> GetSavedShipFiles()
        {
            /*
            Logger.Info($"GetSavedShipFiles called on Instance #{_instanceId}: returning {_staticAvailableShips.Count} ships");
            Logger.Info($"Cache contains {_staticCachedShipData.Count} cached ships");
            foreach (var ship in _staticAvailableShips)
            {
                Logger.Info($"  - Available: {ship}");
            }
            foreach (var cached in _staticCachedShipData.Keys)
            {
                Logger.Info($"  - Cached: {cached}");
            }*/
            // Return list of ships available from server and cached locally
            return new List<string>(_staticAvailableShips);
        }

        public bool HasShipData(string shipName)
        {
            return _staticCachedShipData.ContainsKey(shipName);
        }

        public string? GetShipData(string shipName)
        {
            return _staticCachedShipData.TryGetValue(shipName, out var data) ? data : null;
        }

        private void HandleAdminRequestPlayerShips(AdminRequestPlayerShipsMessage message)
        {
            try
            {
                // Only respond if this is our player ID
                var playerManager = IoCManager.Resolve<Robust.Client.Player.IPlayerManager>();
                if (playerManager.LocalSession?.UserId != message.PlayerId)
                    return;

                var ships = new List<(string filename, string shipName, DateTime timestamp)>();

                // Use cached metadata instead of re-parsing YAML
                foreach (var filename in _staticAvailableShips)
                {
                    if (_staticShipMetadataCache.TryGetValue(filename, out var metadata))
                    {
                        ships.Add((filename, metadata.shipName, metadata.timestamp));
                    }
                    else
                    {
                        // Fallback: cache metadata if not already cached
                        try
                        {
                            CacheShipMetadata(filename);
                            if (_staticShipMetadataCache.TryGetValue(filename, out metadata))
                            {
                                ships.Add((filename, metadata.shipName, metadata.timestamp));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"Failed to get metadata for {filename}: {ex.Message}");
                        }
                    }
                }

                // Send response back to admin
                RaiseNetworkEvent(new AdminSendPlayerShipsMessage(ships, message.AdminName));
                Logger.Info($"Sent {ships.Count} ship details to admin {message.AdminName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to handle admin request for player ships: {ex.Message}");
            }
        }

        private void HandleAdminRequestShipData(AdminRequestShipDataMessage message)
        {
            try
            {
                // Check if we have the requested ship data
                if (_staticCachedShipData.TryGetValue(message.ShipFilename, out var shipData))
                {
                    RaiseNetworkEvent(new AdminSendShipDataMessage(shipData, message.ShipFilename, message.AdminName));
                    Logger.Info($"Sent ship data for {message.ShipFilename} to admin {message.AdminName}");
                }
                else
                {
                    Logger.Warning($"Admin {message.AdminName} requested ship data for {message.ShipFilename} but file not found");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to handle admin request for ship data: {ex.Message}");
            }
        }

        private static string ExtractFileNameWithoutExtension(string filePath)
        {
            var fileName = filePath;
            var lastSlash = filePath.LastIndexOf('/');
            if (lastSlash >= 0)
                fileName = filePath.Substring(lastSlash + 1);
            var lastBackslash = fileName.LastIndexOf('\\');
            if (lastBackslash >= 0)
                fileName = fileName.Substring(lastBackslash + 1);
            var lastDot = fileName.LastIndexOf('.');
            if (lastDot >= 0)
                fileName = fileName.Substring(0, lastDot);
            return fileName;
        }
    }
}
