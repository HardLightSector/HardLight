# Orphaned Grid Cleanup System

## Overview

The Orphaned Grid Cleanup System automatically removes small, insignificant grid fragments that are created when a grid is split (e.g., from explosions, deconstruction, or other grid-severing events). This helps maintain server performance and prevents clutter from accumulating over time.

## How It Works

When a grid split event occurs, the system evaluates each newly created grid against the following criteria:

### Deletion Criteria

A grid is considered "orphaned" and will be deleted if ALL of the following conditions are met:

1. **Size Check**: The grid has fewer than the minimum tile count (default: 5 tiles)
2. **Content Check**: The grid contains no "important" entities

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

The system can be controlled via admin commands or code.

### Admin Commands

Use the `orphanedgridcleanup` command with the following subcommands:

```
orphanedgridcleanup enable          - Enables automatic cleanup
orphanedgridcleanup disable         - Disables automatic cleanup
orphanedgridcleanup settiles <n>    - Sets minimum tile threshold to n
orphanedgridcleanup cleanup <gridId> - Manually clean a specific grid
```

### Code Configuration

```csharp
var cleanupSystem = EntityManager.System<OrphanedGridCleanupSystem>();

// Enable/disable the system
cleanupSystem.SetEnabled(true);

// Set minimum tile count
cleanupSystem.SetMinimumTileCount(10);

// Manually check and cleanup a specific grid
if (cleanupSystem.TryCleanupGrid(gridEntity))
{
    Log.Info("Grid was orphaned and has been deleted");
}
```

## Use Cases

### Explosion Cleanup
When an explosion destroys parts of a ship or station, small debris chunks are automatically removed while preserving any pieces that contain important machinery or survivors.

### Construction/Deconstruction
When players deconstruct large structures, tiny leftover grid fragments are cleaned up automatically.

### Combat Damage
Ships that are damaged in combat may fragment into pieces. Small, non-functional pieces are automatically deleted.

## Performance Considerations

- The system only runs when grid splits occur, not on a periodic timer
- Deletion happens via `QueueDel()`, so it's processed asynchronously
- Entity checks are performed efficiently using component queries
- The system has minimal overhead on normal gameplay

## Tuning

The default minimum tile count of 5 is conservative. You may want to adjust this based on your server's needs:

- **Lower values (1-3)**: More aggressive cleanup, removes even tiny fragments
- **Higher values (10-20)**: More conservative, preserves larger debris fields
- **Very high values (50+)**: Only preserves grids with substantial structure

## Testing

Integration tests are available in `Content.IntegrationTests/Tests/GridSplit/OrphanedGridCleanupTest.cs`:

- Basic cleanup of small orphaned grids
- Preservation of grids with important entities
- Manual cleanup functionality

## Future Enhancements

Potential improvements to consider:

1. **CVar Configuration**: Add CVars for runtime configuration without recompilation
2. **Whitelist/Blacklist**: Allow specific entity types to be explicitly included/excluded
3. **Delay Timer**: Add a grace period before deletion (e.g., 30 seconds) to allow recovery
4. **Notifications**: Alert admins or nearby players when grids are cleaned up
5. **Statistics**: Track cleanup metrics for server monitoring
6. **Per-Map Settings**: Different thresholds for different map types

## Debugging

To debug the system:

1. Check server logs for cleanup messages: `Deleting orphaned grid [entity] created from split of [entity]`
2. Use the manual cleanup command to test specific grids
3. Disable the system temporarily: `orphanedgridcleanup disable`
4. Adjust the tile threshold to see how it affects cleanup behavior

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
