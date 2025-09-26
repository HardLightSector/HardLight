using System;
using Robust.Shared.Serialization;

namespace Content.Shared.Sprite;

/// <summary>
/// Appearance keys for SpriteStateToggle visuals.
/// Separate from generic ToggleVisuals to avoid cross-talk with other systems.
/// </summary>
[Serializable, NetSerializable]
public enum SpriteStateToggleVisuals : byte
{
    Toggled,
}
