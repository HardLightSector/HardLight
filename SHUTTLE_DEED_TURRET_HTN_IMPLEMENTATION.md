# ShuttleDeed-Targeting Turret HTN System Implementation

This implementation adds a new HTN (Hierarchical Task Network) behavior system for the specified turret entities to automatically target and engage grids that have the `ShuttleDeedComponent`.

## Files Created/Modified

### 1. Query System (C#)

**File:** `Content.Server/NPC/Queries/Queries/NearbyShuttleDeedGridsQuery.cs`
- Defines the query data structure for finding nearby grids with ShuttleDeedComponent
- Configurable range (default: 2000 units)
- Supports entity whitelisting/blacklisting via tags

**File:** `Content.Server/NPC/Systems/NPCUtilitySystem.cs` (Modified)
- Added case statement for `NearbyShuttleDeedGridsQuery` in the Consider method
- Searches for grids within range that have `ShuttleDeedComponent` and `MapGridComponent`
- Filters out blacklisted entities and the turret's own grid
- Only considers grids within the specified range

### 2. HTN Prototypes (YAML)

**File:** `Resources/Prototypes/_HL/NPCs/Turrets/turret_htn.yml`

Defines three HTN components:

1. **NearbyShuttleDeedGridTargets** (utilityQuery)
   - Uses `NearbyShuttleDeedGridsQuery` to find targets
   - Applies distance-based scoring with inverse distance consideration
   - Range: 2000 units
   - Blacklist tag: "TurretIgnore"

2. **ShuttleDeedTurretCompound** (htnCompound)
   - Main HTN task for the turret behavior
   - Finds targets using the utility query
   - Calls the attack compound task

3. **ShuttleDeedTurretAttackCompound** (htnCompound)
   - Handles the actual attack behavior
   - Precondition: Target must exist
   - Tasks:
     - Rotate to face target using `RotateToTargetOperator`
     - Fire weapon using `GunOperator`
   - Continuously updates target via UtilityService

### 3. Updated Turret Entities

**File:** `Resources/Prototypes/_HL/Entities/SpaceArtillery/shipguns.yml`

Added HTN component to all seven turret entities:
- AIWeaponTurretSunder
- AIWeaponTurretType35
- AIWeaponLaserTurretApollo
- AIWeaponLaserTurretFlayer
- AIWeaponLaserTurretL1Phalanx
- AIWeaponTurretAdderScattercannon
- AIWeaponTurretCharonette

Each turret now has:
```yaml
- type: HTN
  rootTask:
    task: ShuttleDeedTurretCompound
  blackboard:
    RotateSpeed: !type:Single
      3.141
    SoundTargetInLOS: !type:SoundPathSpecifier
      path: /Audio/Effects/double_beep.ogg
```

## How It Works

1. **Target Acquisition**: The turret continuously scans for grids within 2000 units that have `ShuttleDeedComponent`
2. **Target Prioritization**: Targets are scored based on inverse distance (closer = higher priority)
3. **Rotation**: The turret rotates to face the selected target at π rad/s (180°/s)
4. **Engagement**: Once rotated toward target, the turret fires its weapon
5. **Feedback**: Plays a double beep sound when a target is in line of sight

## Customization Options

### Adjusting Range
Edit the `range` value in the query:
```yaml
- !type:NearbyShuttleDeedGridsQuery
  range: 3000  # Increase to 3000 units
```

### Adjusting Rotation Speed
Modify the `RotateSpeed` in the turret entity:
```yaml
RotateSpeed: !type:Single
  6.283  # Double the speed (2π rad/s = 360°/s)
```

### Adding Blacklist Tags
To make certain grids immune to targeting, add a tag:
```yaml
blacklist:
  tags:
  - TurretIgnore
  - FriendlyShuttle
```

## Testing

1. Build the solution to compile the new C# systems
2. Launch the game
3. Spawn one of the AI turret entities
4. Spawn a grid with `ShuttleDeedComponent` within range
5. The turret should automatically rotate and fire at the grid

## Notes

- The turret only targets grids on the same map (prevents cross-map targeting)
- The HTN system runs continuously and will retarget if a better option appears
- The system uses the same operators as existing shuttle combat AI
- Sound effects provide player feedback when turrets acquire targets

## Future Enhancements

Possible improvements:
- Add faction-based filtering (friend/foe identification)
- Implement leading targets for moving grids
- Add minimum/maximum range constraints
- Create different aggression profiles (passive, defensive, aggressive)
