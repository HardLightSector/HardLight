using System.Diagnostics.CodeAnalysis;
using System.Buffers;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Database;
using Content.Server.Administration.Logs;
using Content.Server.Objectives.Components;
using Content.Server._NF.Bank;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Objectives.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server.Objectives.Systems;

/// <summary>
/// Server system that pays players into their in-character bank account when they complete objectives
/// which are configured with <see cref="ObjectiveRewardComponent"/>.
/// </summary>
public sealed class ObjectiveRewardSystem : EntitySystem
{
    [Dependency] private readonly SharedObjectivesSystem _objectives = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    // Track which objectives are assigned to which mind so we can compute progress.
    private readonly Dictionary<EntityUid, EntityUid> _objectiveToMind = new();
    // Track which objective entities we've already paid out to avoid duplicates.
    private readonly HashSet<EntityUid> _rewarded = new();

    private float _accum;
    private const float ScanInterval = 2.0f; // seconds

    public override void Initialize()
    {
        base.Initialize();

        // When an objective is assigned, start tracking it.
        SubscribeLocalEvent<ObjectiveComponent, ObjectiveAssignedEvent>(OnObjectiveAssigned);

        // Clean up tracking if an objective is deleted or shutdown.
        SubscribeLocalEvent<ObjectiveComponent, ComponentShutdown>(OnObjectiveShutdown);

        // Seed tracking for already-active minds/objectives at startup.
        SeedExistingObjectives();

        // Final sweep at round end to catch anything that completed right at the end.
    SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndTextAppend);
    }

    private void SeedExistingObjectives()
    {
        var query = EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var mindId, out var mind))
        {
            foreach (var obj in mind.Objectives)
            {
                // Only track objectives that actually have a reward component.
                if (!HasComp<ObjectiveRewardComponent>(obj))
                    continue;

                _objectiveToMind[obj] = mindId;
            }
        }
    }

    private void OnObjectiveAssigned(EntityUid uid, ObjectiveComponent comp, ref ObjectiveAssignedEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasComp<ObjectiveRewardComponent>(uid))
            return;

        _objectiveToMind[uid] = args.MindId;
    }

    private void OnObjectiveShutdown(EntityUid uid, ObjectiveComponent comp, ref ComponentShutdown args)
    {
        _objectiveToMind.Remove(uid);
        _rewarded.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accum += frameTime;
        if (_accum < ScanInterval)
            return;
        _accum = 0f;

        ScanAndReward();
    }

    private void OnRoundEndTextAppend(RoundEndTextAppendEvent ev)
    {
        // Do a final pass to award anything that just completed.
        ScanAndReward(isRoundEnd: true);
    }

    private void ScanAndReward(bool isRoundEnd = false)
    {
        // Copy keys since we may mutate tracking on the fly.
        var objectives = ArrayPool<EntityUid>.Shared.Rent(_objectiveToMind.Count);
        var idx = 0;
        foreach (var key in _objectiveToMind.Keys)
            objectives[idx++] = key;

        for (var i = 0; i < idx; i++)
        {
            var objective = objectives[i];

            if (_rewarded.Contains(objective))
                continue;

            if (!TryComp(objective, out ObjectiveComponent? objectiveComp))
                continue; // Deleted or invalid

            if (!TryComp(objective, out ObjectiveRewardComponent? reward))
                continue; // No reward configured

            // For objectives marked as round-end only, skip early periodic payment
            if (reward.OnlyAtRoundEnd && !isRoundEnd)
                continue;

            if (!_objectiveToMind.TryGetValue(objective, out var mindId) || !TryComp(mindId, out MindComponent? mind))
            {
                // Mind mapping lost; stop tracking this objective.
                _objectiveToMind.Remove(objective);
                continue;
            }

            var info = _objectives.GetInfo(objective, mindId, mind);
            if (info == null)
                continue;

            var progress = info.Value.Progress;
            if (progress < 0.999f)
                continue;

            // Completed! Attempt payout once.
            if (TryGetPayoutTarget(mind, out var target))
            {
                if (reward.Amount > 0 && _bank.TryBankDeposit(target.Value, reward.Amount))
                {
                    _rewarded.Add(objective);

                    // Optional feedback
                    if (reward.NotifyPlayer)
                    {
                        var msg = reward.PopupMessage ?? $"Objective complete! You were paid {Content.Shared._NF.Bank.BankSystemExtensions.ToSpesoString(reward.Amount)}.";
                        _popup.PopupEntity(msg, target.Value, Filter.Entities(target.Value), false, PopupType.Small);
                    }

                    var title = info.Value.Title;
                    _adminLog.Add(LogType.Action, LogImpact.Low,
                        $"ObjectiveReward: Paid {reward.Amount} to {ToPrettyString(target.Value)} for completing objective '{title}' (ent {objective}).");
                }
            }
        }

        ArrayPool<EntityUid>.Shared.Return(objectives);
    }

    private bool TryGetPayoutTarget(MindComponent mind, [NotNullWhen(true)] out EntityUid? target)
    {
        // Prefer the currently owned entity (most reliable for an active player and has the BankAccountComponent).
        if (mind.OwnedEntity is { } owned && EntityManager.EntityExists(owned) && HasComp<BankAccountComponent>(owned))
        {
            target = owned;
            return true;
        }

        // Fallback: the original owned entity if it still exists and has a bank account.
    var original = GetEntity(mind.OriginalOwnedEntity);
        if (original is { } orig && EntityManager.EntityExists(orig) && HasComp<BankAccountComponent>(orig))
        {
            target = orig;
            return true;
        }

        target = null;
        return false;
    }
}
