## Overview

## Types
Server.ConsentManager:
    Source of truth for player consent settings.
    Saves and loads settings to and from the db.
    Public API for getting consent settings outside of the gamesim, from a NetUserId. (You should almost never need to use this)

Client.ConsentManager:
    Stores the local player's consent settings.
    Sends the consent settings to the server when the client updates it.

Shared.ConsentComponent:
    Stores the entity's consent settings.
    On players, this always mirrors the player's consent settings in Server.ConsentManager. (Needed for prediction)
    On non-player entities, it can be set but will be overridden if a player takes control of it. So you can use this comp if you want a non-player controlled mob to never "consent" to a toggle, but are fine with it consenting while being controlled by a player. If you want a mob to **never** be subject to a certain mechanic, you should have a component that blocks it and check for that in addition to checking consent.

Shared.ConsentSystem:
    Adds the examine verb for checking the consent/preferences freetext of an entity.
    Public API for cheking a consent toggle of an entity.

Server.ConsentSystem:
    Responsible for mirroring the consent settings from `ConsentManager` onto `ConsentComponent`s

## Changelog

### 1.0
- Initial release

### 1.1
- Make the UI auto-generate the toggle buttons.
- Make it easier to localize consent toggles.

### 2.0
- Move code to _Common folder and add MIT spdx headers.
- Add a new ConsentComponent, to allow for client prediction and for non-player entities to have consent settings.
- Add read recipes, displaying a red dot if the text has changed since you last read it.
- Allow a limited set of markup tags in the consent text.
- Add an event that's raised when an entity changes one of its consent toggles.
- Allow markup in consent toggle name/description.
- Add ConsentCondition for entity/reagent effects.
