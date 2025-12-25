# Orphaned Grid Cleanup System

## Overview

The Orphaned Grid Cleanup System automatically removes small, insignificant grid fragments that are created when a grid is split (e.g., from explosions, deconstruction, or other grid-severing events), as well as empty/nameless grids that spawn during gameplay. This helps maintain server performance and prevents clutter from accumulating over time.

## How It Works

The system has two cleanup modes:

### 1. Split-Based Cleanup
When a grid split event occurs, the system evaluates each newly created grid against cleanup criteria and immediately removes those that don't meet the threshold.

### 2. Periodic Empty Grid Cleanup
The system periodically scans all grids and removes those that:
- Have zero tiles (completely empty grids)
- Have a blank/empty name AND fewer tiles than the minimum threshold
- Are old enough (configurable minimum age to avoid deleting grids being populated)
- Don't contain important entities

## Deletion Criteria

A grid is considered "orphaned" and will be deleted if ALL of the following conditions are met:

1. **Size Check**: The grid has fewer than the minimum tile count (default: 5 tiles)
2. **Content Check**: The grid contains no "important" entities
3. **Age Check** (for periodic cleanup): The grid has existed for longer than the minimum age (default: 30 seconds)

### Special Cases

The following grid types receive special handling:

- **Dungeon Atlas Templates**: Grids with `DungeonAtlasTemplateComponent` are pre-loaded dungeon templates that should be cleaned up if they persist with few tiles. These are normally deleted on round start but may sometimes remain.

### Important Entities

The following types of entities are considered important and will prevent grid deletion:

- **Players**: Any entity with an ActorComponent (player-controlled characters)
- **Mobs**: NPCs, animals, or any entity with a MobStateComponent
- **Power Producers**: Generators, AMEs, solar panels, or any PowerSupplierComponent
- **Significant Machinery**: Any PowerConsumerComponent drawing more than 100W
- **Station Components**: Any entity that is part of a station (StationMemberComponent)
- **Doors/Airlocks**: Any DoorComponent (indicates functional structure)
- **APCs**: Area Power Controllers

## Configuration

The system can be controlled via CVars, admin commands, or code.

### CVars

```
shuttle.orphaned_grid_cleanup_enabled     - Enable/disable split-based cleanup (default: true)
shuttle.orphaned_grid_minimum_tiles       - Minimum tile count threshold (default: 5)
shuttle.empty_grid_cleanup_enabled        - Enable/disable periodic cleanup (default: true)
shuttle.empty_grid_cleanup_interval       - How often to run periodic cleanup in seconds (default: 60)
shuttle.empty_grid_cleanup_min_age        - Minimum grid age before cleanup in seconds (default: 30)
```

### Admin Commands

Use the `orphanedgridcleanup` command with the following subcommands:

```
orphanedgridcleanup enable          - Enables automatic split-based cleanup
orphanedgridcleanup disable         - Disables automatic split-based cleanup
orphanedgridcleanup enableempty     - Enables periodic empty grid cleanup
orphanedgridcleanup disableempty    - Disables periodic empty grid cleanup
orphanedgridcleanup force           - Forces an immediate empty grid cleanup check
orphanedgridcleanup settiles <n>    - Sets minimum tile threshold to n
orphanedgridcleanup cleanup <gridId> - Manually clean a specific grid
```

### Code Configuration

```csharp
var cleanupSystem = EntityManager.System<OrphanedGridCleanupSystem>();

// Enable/disable split-based cleanup
cleanupSystem.SetEnabled(true);

// Enable/disable periodic empty grid cleanup
cleanupSystem.SetEmptyGridCleanupEnabled(true);

// Set minimum tile count
cleanupSystem.SetMinimumTileCount(10);

// Manually check and cleanup a specific grid
if (cleanupSystem.TryCleanupGrid(gridEntity))
{
    Log.Info("Grid was orphaned and has been deleted");
}

// Force immediate empty grid cleanup
int deletedCount = cleanupSystem.ForceEmptyGridCleanup();
```

## Use Cases

### Explosion Cleanup
When an explosion destroys parts of a ship or station, small debris chunks are automatically removed while preserving any pieces that contain important machinery or survivors.

### Construction/Deconstruction
When players deconstruct large structures, tiny leftover grid fragments are cleaned up automatically.

### Combat Damage
Ships that are damaged in combat may fragment into pieces. Small, non-functional pieces are automatically deleted.

### Blank Grid Spawning (NEW)
Some game systems may spawn grids that end up empty or without proper names. These are now automatically cleaned up after a grace period.

## Performance Considerations

- Split-based cleanup runs only when grid splits occur, not on a periodic timer
- Periodic cleanup runs every 60 seconds by default (configurable)
- Deletion happens via `QueueDel()`, so it's processed asynchronously
- Entity checks are performed efficiently using component queries
- The system tracks grid creation times with minimal memory overhead
- The system has minimal overhead on normal gameplay

## Tuning

The default minimum tile count of 5 is conservative. You may want to adjust this based on your server's needs:

- **Lower values (1-3)**: More aggressive cleanup, removes even tiny fragments
- **Higher values (10-20)**: More conservative, preserves larger debris fields
- **Very high values (50+)**: Only preserves grids with substantial structure

The minimum age parameter prevents newly spawned grids from being deleted before they can be populated:

- **Lower values (10-15s)**: Faster cleanup of truly empty grids
- **Higher values (60-120s)**: More time for grids to be populated before cleanup check

## Testing

Integration tests are available in `Content.IntegrationTests/Tests/GridSplit/OrphanedGridCleanupTest.cs`:

- Basic cleanup of small orphaned grids
- Preservation of grids with important entities
- Manual cleanup functionality

## Debugging

To debug the system:

1. Check server logs for cleanup messages:
   - `Deleting orphaned grid [entity] created from split of [entity]`
   - `Deleting empty/nameless grid [entity] during periodic cleanup`
   - `Periodic empty grid cleanup deleted X grid(s)`
2. Use the `orphanedgridcleanup force` command to trigger immediate cleanup
3. Use the `orphanedgridcleanup disable` / `orphanedgridcleanup disableempty` commands to disable cleanup temporarily
4. Adjust the tile threshold with `orphanedgridcleanup settiles <count>`
5. Check CVars with `cvar shuttle.empty_grid_cleanup_enabled` etc.

## Example Scenarios

### Scenario 1: Explosion on a Ship
A bomb detonates on a ship, splitting it into 3 pieces:
- Piece A: 200 tiles, contains bridge and crew → **Preserved**
- Piece B: 50 tiles, contains engine room → **Preserved**
- Piece C: 3 tiles, just wall fragments → **Deleted**

### Scenario 2: Deconstructing a Station Wing
A player deconstructs part of a station:
- Main station: 10,000 tiles → **Preserved**
- Small debris: 2 tiles each → **Deleted**
- A 4-tile piece with an airlock → **Preserved** (has important entity)

### Scenario 3: Mining Accident
An asteroid gets split during mining:
- Large asteroid chunk: 500 tiles → **Preserved**
- Small fragments: 1-3 tiles → **Deleted**
- Fragment with a trapped miner: 2 tiles → **Preserved** (has player)

### Scenario 4: Blank Grid Spawning (NEW)
A system spawns grids during gameplay:
- Grid with 0 tiles, 45 seconds old → **Deleted** (periodic cleanup)
- Grid with 0 tiles, 10 seconds old → **Preserved** (too new, might still be populated)
- Grid named "grid" with 3 tiles, 60 seconds old → **Deleted** (nameless and small)
- Grid named "Shuttle Alpha" with 3 tiles → **Preserved** (has a proper name)
