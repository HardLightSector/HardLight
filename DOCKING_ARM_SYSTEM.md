# Docking Arm Generator System

## Overview
The Docking Arm Generator System is inspired by the Gateway Generator system but instead of creating procedural dungeons, it spawns specific pre-made grid structures (docking arms) onto the current map when selected through a gateway interface.

## Components

### DockingArmGeneratorComponent
Located at: `Content.Server/Gateway/Components/DockingArmGeneratorComponent.cs`

This component should be added to a station entity to enable docking arm generation.

**Fields:**
- `Proto` (EntProtoId): The gateway entity to spawn (default: "Gateway")
- `NextUnlock` (TimeSpan): When the next docking arm option becomes available
- `UnlockCooldown` (TimeSpan): Time between unlocks (default: 30 minutes)
- `Generated` (List<EntityUid>): List of generated destination maps
- `DockingArmGrids` (List<string>): YAML file paths for docking arm structures
- `InitialCount` (int): Number of docking arms to generate at start (default: 3)
- `MaxSpawnDistance` (int): Max distance from origin to spawn (default: 256)
- `MinSpawnDistance` (int): Min distance from origin to spawn (default: 64)

### DockingArmDestinationComponent
Located at: `Content.Server/Gateway/Components/DockingArmDestinationComponent.cs`

This component is automatically added to destination maps created by the generator.

**Fields:**
- `Generator` (EntityUid): The generator that created this destination
- `Locked` (bool): Whether this destination is locked/unavailable
- `Loaded` (bool): Whether the docking arm grid has been spawned
- `Name` (string): Display name of this docking arm
- `GridPath` (string): Path to the YAML file for this docking arm
- `Origin` (Vector2i): Spawn coordinates for the gateway
- `DockingArmGrid` (EntityUid?): The spawned docking arm grid entity

## System

### DockingArmGeneratorSystem
Located at: `Content.Server/Gateway/Systems/DockingArmGeneratorSystem.cs`

**Key Methods:**
- `GenerateDestination()`: Creates a new docking arm destination with a gateway
- `OnDockingArmOpen()`: Handles when a player selects a docking arm, spawning the grid
- `SpawnDockingArmGrid()`: Loads the docking arm grid from YAML onto the target map

## How It Works

1. **Initialization**: When a station with `DockingArmGeneratorComponent` initializes, it creates `InitialCount` destination maps, each with:
   - A small landing pad (5x5 steel tiles)
   - A gateway entity at the origin
   - A randomly selected grid path from `DockingArmGrids`

2. **Selection**: Players can interact with any gateway on the station to see available docking arm destinations

3. **Spawning**: When a player selects an unlocked docking arm destination:
   - The gateway opens a portal to that destination map
   - The system loads the docking arm grid from the YAML file
   - The grid is spawned at a random distance (20-40 tiles) from the gateway origin
   - The grid is placed on the same map as the source gateway (typically the station map)
   - A new destination is generated to maintain available options

4. **Cooldown**: After selection, there's a cooldown before the next destination unlocks

## Usage Example

### Station Configuration
```yaml
- type: entity
  id: MyStation
  parent: BaseStation
  components:
  - type: DockingArmGenerator
    unlockCooldown: 1800  # 30 minutes
    initialCount: 3
    dockingArmGrids:
      - /Maps/Structures/docking_arm_alpha.yml
      - /Maps/Structures/docking_arm_beta.yml
      - /Maps/Structures/docking_arm_gamma.yml
```

### Creating Docking Arm Grids
Docking arm grids are standard grid YAML files. Create them using the map editor:

1. Build your docking arm structure in the map editor
2. Save as a grid (not a full map)
3. Place the .yml file in `/Resources/Maps/`
4. Add the path to the `dockingArmGrids` list

**Recommended structure:**
- Include airlocks for docking
- Add a small power supply or APCs
- Include basic facilities (cargo bay, medical, etc.)
- Keep it relatively small (suggested: 10x20 to 30x30 tiles)

## Differences from Gateway Generator

| Feature | Gateway Generator | Docking Arm Generator |
|---------|------------------|----------------------|
| Content Type | Procedural dungeons on distant maps | Pre-made grids on station map |
| Biomes | Yes (planetary) | No |
| Grid Loading | Dungeon generation | YAML grid loading |
| Spawn Location | Separate map | Same map as station |
| Mobs/Loot | Biome marker layers | Whatever is in the YAML |
| Use Case | Exploration/adventure | Station expansion |

## Integration with Gateway System

The system integrates with the existing Gateway UI and portal mechanics:
- **UI Detection**: The gateway UI checks for `DockingArmDestinationComponent` and displays "Spawn Docking Arm" instead of "Open Portal"
- **Message Handling**: Uses `GatewaySpawnDockingArmMessage` for client-server communication
- **Event System**:
  - `AttemptGatewayOpenEvent`: Validates access and unlock status
  - `GatewayOpenEvent`: Triggers grid spawning via `DockingArmGeneratorSystem`
- **UI Updates**: `GatewaySystem.UpdateAllGateways()` refreshes available destinations

### UI Behavior
- **Regular destinations**: Show "Open Portal" button (toggle mode, creates temporary portals)
- **Docking arm destinations**: Show "Spawn Docking Arm" button (one-time spawn, permanent)
- **Locked destinations**: Docking arm destinations still show the spawn button even when locked (regular destinations hide the portal button when locked)
- **Already spawned**: Button is disabled if `Loaded` is true to prevent duplicates

### Localization Keys
```fluent
gateway-window-spawn-docking-arm = Spawn Docking Arm
gateway-docking-arm-already-spawned = Docking arm already spawned!
gateway-docking-arm-spawn-failed = Failed to spawn docking arm!
gateway-docking-arm-spawned = Docking arm spawned successfully!
```

## Technical Notes

- The system uses `MapLoaderSystem.TryLoadGrid()` to spawn grids
- Grids are spawned with a position offset from the gateway origin
- The destination map is separate from the spawned grid's final location
- Names can be customized via the `names_borer` dataset prototype
- The system respects the `GatewayGeneratorEnabled` CVar

## Future Enhancements

Potential improvements:
- Cost/resource requirements for spawning
- Per-grid unlock requirements
- Custom spawn positioning logic
- Integration with construction/engineering
- Admin tools for manual spawning
- Grid rotation options
