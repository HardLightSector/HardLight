using System.Diagnostics.CodeAnalysis;
using System.Linq; // For OrderByDescending / LINQ
using Content.Shared.HL.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Content.Server.Diagnostics;

/// <summary>
/// Lightweight runtime profiler that samples entity spawn rates by prototype.
/// Helps identify unexpected background spawning (e.g. runaway worldgen or subsystem churn)
/// when developers observe large gaps in sequential entity IDs during manual spawning.
///
/// Controlled via CVars:
///  - hl.profiler.entity_spawns.enabled (bool)
///  - hl.profiler.entity_spawns.interval (seconds)
///  - hl.profiler.entity_spawns.top (rows)
/// </summary>
public sealed class EntitySpawnProfilerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<string, int> _prototypeCounts = new();
    private TimeSpan _lastReportTime;
    private bool _enabledCached;
    private float _intervalCached;
    private int _topCached;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("spawn-profiler");
        _lastReportTime = _timing.CurTime;
        CacheCvars();

        // React to live CVar changes.
        _cfg.OnValueChanged(HLProfilerCCVars.EntitySpawnProfilerEnabled, _ => CacheCvars(), true);
        _cfg.OnValueChanged(HLProfilerCCVars.EntitySpawnProfilerInterval, _ => CacheCvars(), true);
        _cfg.OnValueChanged(HLProfilerCCVars.EntitySpawnProfilerTop, _ => CacheCvars(), true);

        EntityManager.EntityAdded += OnEntityAdded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        EntityManager.EntityAdded -= OnEntityAdded;
    }

    private void CacheCvars()
    {
        _enabledCached = _cfg.GetCVar(HLProfilerCCVars.EntitySpawnProfilerEnabled);
        _intervalCached = MathF.Max(0.5f, _cfg.GetCVar(HLProfilerCCVars.EntitySpawnProfilerInterval));
        _topCached = Math.Max(1, _cfg.GetCVar(HLProfilerCCVars.EntitySpawnProfilerTop));
    }

    private void OnEntityAdded(Entity<MetaDataComponent> ent)
    {
        if (!_enabledCached)
            return;

        var meta = ent.Comp;
        if (meta.EntityPrototype == null)
            return;

        var id = meta.EntityPrototype.ID;
        if (_prototypeCounts.TryGetValue(id, out var count))
            _prototypeCounts[id] = count + 1;
        else
            _prototypeCounts[id] = 1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabledCached)
            return;

        var now = _timing.CurTime;
        if ((now - _lastReportTime).TotalSeconds < _intervalCached)
            return;

        _lastReportTime = now;
        if (_prototypeCounts.Count == 0)
        {
            _sawmill.Info("[EntitySpawnProfiler] No spawns in interval.");
            return;
        }

        var total = 0;
        foreach (var v in _prototypeCounts.Values)
            total += v;

        // Order by descending count and take top N.
        var top = _prototypeCounts
            .OrderByDescending(p => p.Value)
            .Take(_topCached)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append($"[EntitySpawnProfiler] {total} spawns in last {_intervalCached:F1}s | Top {_topCached} prototypes:\n");
        foreach (var pair in top)
        {
            var proto = pair.Key;
            var count = pair.Value;
            var pct = (double)count / total * 100.0;
            sb.AppendFormat("  {0,-40} {1,6} ({2,5:0.0}% )\n", proto, count, pct);
        }

        _sawmill.Info(sb.ToString().TrimEnd());
        _prototypeCounts.Clear();
    }
}
