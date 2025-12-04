# QUICK START: Test Docking Arm System

# This file shows you EXACTLY how to test the docking arm system

## Method 1: Auto-Generated Destinations (Recommended)

# 1. In your map file, add DockingArmGenerator to your station:

# entities:
# - uid: 1
#   type: BaseStation
#   components:
#   - type: StationData
#   - type: DockingArmGenerator
#     proto: Gateway
#     initialCount: 2
#     dockingArmGrids:
#       - /Maps/_NF/POI/docking_arm.yml  # MUST CREATE THIS FILE
#   - type: Transform

# 2. Place a Gateway entity somewhere on your station:

# - uid: 100
#   type: Gateway
#   components:
#   - type: Transform
#     parent: 2  # Parent to your station grid
#     pos: 10,10
#   - type: Gateway
#     enabled: true

# 3. Create a simple docking arm grid file at /Maps/_NF/POI/docking_arm.yml

## Method 2: Manual Console Command (For Testing)

# If you just want to test, you can manually create a destination:
#
# 1. Run server
# 2. Open console (F12)
# 3. Run these commands:
#
# createmap
# (note the map ID it gives you, e.g., "Map 2")
#
# addcomp <map-entity-uid> DockingArmDestination
# setcompfield <map-entity-uid> DockingArmDestination gridPath /Maps/_NF/POI/docking_arm.yml
# setcompfield <map-entity-uid> DockingArmDestination locked false
#
# tp <map-entity-uid> 0 0
# spawn Gateway
# (select the gateway entity)
# setcompfield <gateway-uid> Gateway enabled true
#
# Now open any other gateway and you should see the docking arm destination!

## Most Common Issues:

# 1. NO DESTINATIONS SHOW UP
#    - You didn't add DockingArmGenerator to your station entity
#    - Or: No gateways have been spawned yet
#
# 2. DESTINATIONS SHOW BUT NO "SPAWN DOCKING ARM" BUTTON
#    - The destination map doesn't have DockingArmDestinationComponent
#    - Check server console for debug logs
#
# 3. BUTTON SHOWS BUT IS DISABLED
#    - locked: true (wait for unlock timer or set to false)
#    - Already spawned (loaded: true)

## Required Files:

# You MUST create at least one docking arm grid file, for example:
# /Resources/Maps/_NF/POI/docking_arm.yml

# Minimal example:

# meta:
#   format: 6
#   postmapinit: false
#
# tilemap:
#   0: Space
#   1: FloorSteel
#
# grids:
# - type: grid
#   settings:
#     chunksize: 16
#   chunks:
#   - ind: 0,0
#     tiles: "AAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAA=="
#
# entities:
# - uid: 1
#   components:
#   - type: Transform
#   - type: MapGrid
#   - type: GridPathfinding
#
# - uid: 2
#   type: Airlock
#   components:
#   - type: Transform
#     parent: 1
#     pos: 5,5
