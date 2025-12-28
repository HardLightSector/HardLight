# Xenoborg Drone Spawn System

## Overview
Xenoborg autonomous combat drones now spawn as rare hostile encounters in deep space (beyond 5.5K distance from spawn).

## Implementation Details

### Map Files
Located in `Resources/Prototypes/_HL/Maps/drones/`:
- **lbit.yml** - 34 entities, 1 AIWeaponLaserTurretApollo (smallest)
- **lbyte.yml** - 68 entities, 2 AIWeaponTurretSunder
- **lgbyte.yml** - 100 entities, 4 AIWeaponLaserTurretL1Phalanx, 2 AIWeaponTurretType35
- **mbyte.yml** - 100 entities, 2 AIWeaponLaserTurretFlayer, 1 AIWeaponTurretCharonette
- **mgbyte.yml** - 108 entities, 3 AIWeaponTurretCharonette
- **mtbyte.yml** - 212 entities, loot spawners, 1 AIWeaponTurretAdderScattercannon, 1 AIWeaponTurretCharonette, 3 AIWeaponTurretType35 (largest, rare)
- **sgbyte.yml** - 104 entities, 4 AIWeaponLaserTurretL1Phalanx, 2 AIWeaponTurretAdderScattercannon

### Entity Prototypes
File: `Resources/Prototypes/_HL/Entities/World/Debris/xenoborg_drones.yml`

Base components:
- GridSpawner - Loads map file when spawned
- IFF (Red #ff4444) - Marks as hostile
- SpaceDebris - Tracks for cleanup

### Spawn Configuration
File: `Resources/Prototypes/_NF/World/Biomes/basic.yml`

Biome: **NFAsteroidsFar** (5K+ distance, no upper limit)

Spawn probabilities in `NoiseDrivenDebrisSelector`:
- XenoborgDroneLbit: 0.5%
- XenoborgDroneLbyte: 0.5%
- XenoborgDroneLgbyte: 0.5%
- XenoborgDroneMbyte: 0.5%
- XenoborgDroneMgbyte: 0.5%
- **XenoborgDroneMtbyte: 0.3%** (larger/rare)
- XenoborgDroneSgbyte: 0.5%

Uses `orGroup: wreck` so only one wreck/drone spawns per noise point.

### How It Works

1. **World Generation**: Beyond 5K, the NFAsteroidsFar biome controls spawning
2. **Noise Evaluation**: NoiseDrivenDebrisSelector uses "Wreck" channel to determine spawn points
3. **Probability Roll**: Each spawn point has ~3.3% total chance for Xenoborg drone vs regular wrecks
4. **Grid Loading**: GridSpawnerComponent loads the .yml map file at the location
5. **HTN AI**: All weapons have AIShipWeapon tag and ShipTargetingSystem handles combat
6. **Radar Visualization**: Hitscan weapons show red fire lines on shuttle radar

### Combat Behavior

All turrets use the ShipTargetingSystem HTN AI:
- **Target Acquisition**: Scans for ShuttleDeed grids within range
- **Firing Pattern**: Shoots forward periodically when targets detected
- **Radar Signature**: Red hitscan lines appear on victim's radar when fired

### Balance

- **Very Low Spawn Rate**: 0.3-0.5% per wreck spawn point
- **Distance Gated**: Only spawns beyond 5K (5500 units)
- **Variant Diversity**: 7 different drone sizes/loadouts
- **Predictable Threat**: AI shoots forward, players can dodge
- **Visual Warning**: Hitscan radar shows incoming fire

### Testing

To test in-game:
1. Launch/purchase a shuttle
2. Travel beyond 5.5K distance from spawn
3. Use shuttle radar to scan for debris
4. Red IFF markers indicate hostile Xenoborg drones
5. Approach with caution - drones will open fire on detection

### Troubleshooting

**No drones spawning?**
- Ensure you're beyond 5K distance
- Spawn rate is very low (0.3-0.5%) - may take exploration
- Check server logs for "Failed to place debris" errors

**Drones not shooting?**
- HTN AI requires ShipTargetingSystem to be loaded
- Weapons need AIShipWeapon tag (already configured)
- Check that shuttle has ShuttleDeed component

**Build errors?**
- Verify all 7 map files exist in `Resources/Prototypes/_HL/Maps/drones/`
- Check prototype syntax in xenoborg_drones.yml
- Ensure SpaceDebrisComponent is defined in codebase

## Files Modified/Created

### Created:
- `Resources/Prototypes/_HL/Entities/World/Debris/xenoborg_drones.yml`
- `Resources/Prototypes/_HL/Maps/drones/lbit.yml`
- `Resources/Prototypes/_HL/Maps/drones/lbyte.yml`
- `Resources/Prototypes/_HL/Maps/drones/lgbyte.yml`
- `Resources/Prototypes/_HL/Maps/drones/mbyte.yml`
- `Resources/Prototypes/_HL/Maps/drones/mgbyte.yml`
- `Resources/Prototypes/_HL/Maps/drones/mtbyte.yml`
- `Resources/Prototypes/_HL/Maps/drones/sgbyte.yml`

### Modified:
- `Resources/Prototypes/_NF/World/Biomes/basic.yml` (added 7 drone entries to NFAsteroidsFar)

## System Architecture

```
World Generation (Beyond 5K)
    ↓
NFAsteroidsFar Biome
    ↓
NoiseDrivenDebrisSelector
    ↓
PrePlaceDebrisFeatureEvent (distance/noise filters)
    ↓
TryGetPlaceableDebrisFeatureEvent (selects XenoborgDrone*)
    ↓
GridSpawnerComponent (loads map .yml)
    ↓
ShipTargetingSystem HTN AI (engages targets)
    ↓
HitscanRadarVisualizationSystem (shows fire on radar)
```
