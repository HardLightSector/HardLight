# ToggleableClothing System - Example Usage

The ToggleableClothing system has been updated to work with any clothing slot, not just the head slot. Here's how to use it:

## Example 1: Belt with built-in storage pouch
```yaml
- type: entity
  parent: ClothingBeltBase
  id: ClothingBeltWithPouch
  name: utility belt with pouch
  description: A utility belt with a detachable storage pouch.
  components:
  - type: Sprite
    sprite: Clothing/Belt/utility.rsi
  - type: ToggleableClothing
    clothingPrototype: ClothingPouchUtility  # The pouch that gets equipped/unequipped
    # No need to specify 'slot' - auto-detected from ClothingPouchUtility's slot configuration
  - type: ContainerContainer
    containers:
      toggleable-clothing: !type:ContainerSlot {}
```

## Example 2: Jacket with detachable hood
```yaml
- type: entity
  parent: ClothingOuterBase
  id: ClothingOuterJacketWithHood
  name: winter jacket
  description: A warm jacket with a detachable hood.
  components:
  - type: Sprite
    sprite: Clothing/OuterClothing/WinterCoats/jacket.rsi
  - type: ToggleableClothing
    clothingPrototype: ClothingHeadHatHoodWinter  # The hood that gets equipped/unequipped
    # No need to specify 'slot' - auto-detected from ClothingHeadHatHoodWinter's slot configuration
  - type: ContainerContainer
    containers:
      toggleable-clothing: !type:ContainerSlot {}
```

## Example 3: Jumpsuit with detachable belt
```yaml
- type: entity
  parent: ClothingUniformBase
  id: ClothingUniformJumpsuitWithBelt
  name: utility jumpsuit
  description: A practical jumpsuit with a detachable utility belt.
  components:
  - type: Sprite
    sprite: Clothing/Uniforms/jumpsuit_utility.rsi
  - type: ToggleableClothing
    clothingPrototype: ClothingBeltUtility        # The belt that gets equipped/unequipped
    # No need to specify 'slot' - auto-detected from ClothingBeltUtility's slot configuration
  - type: ContainerContainer
    containers:
      toggleable-clothing: !type:ContainerSlot {}
```

## Key Changes Made:

1. **Removed hardcoded RequiredFlags**: The system now dynamically determines the required SlotFlags based on the slot name.

2. **Added automatic slot detection**: The system now automatically determines where the toggled clothing should be equipped by examining the `clothingPrototype`'s ClothingComponent slots.

3. **Simplified configuration**: You no longer need to specify the `slot` field - it's automatically detected from the clothing prototype.

4. **Added GetRequiredFlagsForSlot() method**: This maps slot names to their corresponding SlotFlags automatically:
   - "head" → SlotFlags.HEAD
   - "belt" → SlotFlags.BELT
   - "back" → SlotFlags.BACK
   - "feet"/"shoes" → SlotFlags.FEET
   - "neck" → SlotFlags.NECK
   - etc.

5. **Maintained backward compatibility**: Existing items that manually specify `slot:` will continue to work, but it's no longer necessary.

6. **No other code changes needed**: The system works out of the box for any valid inventory slot without requiring modifications to other systems.

## Supported Slots:
All standard inventory slots are supported: head, eyes, ears, mask, outerclothing, innerclothing/jumpsuit, neck, back, belt, gloves, id, pocket, feet/shoes, suitstorage, wallet.
