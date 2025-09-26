# MetaDataComponent Resolution Error Fix

## Problem Identified
The server console error `"[ERRO] resolve: can't resolve "robust.shared.gameobjects.metadatacomponent" on entity"` occurs because the round persistence system was calling `MetaData(entityUid).EntityName` on entities that might be:

1. **Being deleted during round transitions**
2. **Already deleted but still referenced**
3. **Missing the MetaDataComponent**
4. **In an invalid state during entity lifecycle changes**

## Root Cause
The persistence system runs during round restarts when entities are being created, deleted, and modified rapidly. Accessing components without validation during this period causes resolution failures.

## Fixes Applied

### 1. Safe MetaData Access Pattern
**Before (Unsafe):**
```csharp
var stationName = MetaData(stationUid).EntityName;
```

**After (Safe):**
```csharp
if (TryComp<MetaDataComponent>(stationUid, out var stationMeta))
    var stationName = stationMeta.EntityName;
else
    var stationName = stationUid.ToString(); // Fallback
```

### 2. Entity Validation Before Access
**Added validation checks:**
```csharp
if (!EntityExists(entityUid) || TerminatingOrDeleted(entityUid) || !TryComp<MetaDataComponent>(entityUid, out var meta))
{
    _sawmill.Warning($"Skipping invalid entity {entityUid}");
    return; // or continue;
}
```

### 3. Added Utility Method
Created `GetSafeEntityName(EntityUid)` helper method for consistent safe access.

## Files Modified
- `Content.Server\_HL\RoundPersistence\Systems\RoundPersistenceSystem.cs`
- `Content.Server\_HL\RoundPersistence\Systems\PlayerPaymentPersistenceSystem.cs`

## Expected Results
- **Eliminates MetaDataComponent resolution errors**
- **Prevents crashes during round transitions**
- **Adds graceful degradation when entities are invalid**
- **Maintains system functionality while improving stability**

## Testing
1. Monitor server console for the MetaDataComponent error
2. Perform round restarts and observe logs
3. Verify persistence system still functions correctly
4. Check that ships and player data are preserved across rounds

The fixes maintain all existing functionality while preventing the component resolution errors that occur during entity lifecycle changes.
