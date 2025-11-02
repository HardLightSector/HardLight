using Robust.Shared.GameStates;

namespace Content.Server.Objectives.Components;

/// <summary>
/// Attach to an objective entity prototype to award bank currency when completed.
/// Amount is awarded once per player the first time the objective is detected as complete.
/// </summary>
[RegisterComponent]
public sealed partial class ObjectiveRewardComponent : Component
{
    /// <summary>
    /// Amount of currency to deposit when the objective is completed.
    /// </summary>
    [DataField(required: true)]
    public int Amount;

    /// <summary>
    /// Optional popup message shown to the player on payout. Supports string.Format with the amount.
    /// </summary>
    [DataField]
    public string? PopupMessage;

    /// <summary>
    /// Whether to show a popup to the player when they are paid.
    /// </summary>
    [DataField]
    public bool NotifyPlayer = true;

    /// <summary>
    /// If true, the reward is only paid during the final round-end sweep and not during periodic progress scans.
    /// Useful for survive-style objectives so players aren't paid immediately mid-round.
    /// </summary>
    [DataField]
    public bool OnlyAtRoundEnd = false;
}
