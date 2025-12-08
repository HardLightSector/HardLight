using Content.Server._HL.RoundPersistence.Components;
using Content.Server._NF.RoundNotifications.Events;
using Content.Server.GameTicking.Events;
using Content.Server.Mind;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Players;
using Content.Shared.Roles;
using Robust.Shared.Enums;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.HL.RoundPersistence.Systems;

/// <summary>
/// Handles persistence of player payment/autopay data across round restarts
/// </summary>
public sealed class PlayerPaymentPersistenceSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// Track player work sessions
    /// </summary>
    private readonly Dictionary<ICommonSession, PlayerWorkSession> _activeSessions = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("player-payment-persistence");

        // Listen for player connection events
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        // Listen for job changes - use role events instead of job events
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAdded);
        SubscribeLocalEvent<RoleRemovedEvent>(OnRoleRemoved);

        // Listen for round events
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        switch (e.NewStatus)
        {
            case SessionStatus.Connected:
                StartPlayerSession(e.Session);
                break;
            case SessionStatus.Disconnected:
                EndPlayerSession(e.Session);
                break;
        }
    }

    private void OnRoleAdded(RoleAddedEvent args)
    {
        if (_playerManager.TryGetSessionById(args.Mind.UserId, out var session))
        {
            // We could track job role additions here if needed
            // For now, just update timing for any role change
            UpdatePlayerJob(session, null);
        }
    }

    private void OnRoleRemoved(RoleRemovedEvent args)
    {
        if (_playerManager.TryGetSessionById(args.Mind.UserId, out var session))
        {
            // Track role removals
            UpdatePlayerJob(session, null);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        SaveAllPlayerPaymentData();
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        RestorePlayerPaymentData();
    }

    private void StartPlayerSession(ICommonSession session)
    {
        if (_activeSessions.ContainsKey(session))
            return;

        _activeSessions[session] = new PlayerWorkSession
        {
            PlayerName = session.Name,
            UserId = session.UserId.ToString(),
            SessionStartTime = DateTime.UtcNow,
            CurrentJob = "Unknown"
        };

        _sawmill.Debug($"Started payment tracking for player {session.Name}");
    }

    private void EndPlayerSession(ICommonSession session)
    {
        if (!_activeSessions.TryGetValue(session, out var workSession))
            return;

        workSession.SessionEndTime = DateTime.UtcNow;

        // Calculate hours worked this session
        var sessionDuration = workSession.SessionEndTime.Value - workSession.SessionStartTime;
        workSession.HoursWorked = (float)sessionDuration.TotalHours;

        _sawmill.Debug($"Ended payment tracking for player {session.Name}, worked {workSession.HoursWorked:F2} hours");

        _activeSessions.Remove(session);
    }

    private void UpdatePlayerJob(ICommonSession session, string? jobId)
    {
        if (!_activeSessions.TryGetValue(session, out var workSession))
            return;

        if (workSession.CurrentJob != jobId)
        {
            workSession.LastJobChange = DateTime.UtcNow;
            workSession.CurrentJob = jobId ?? "Unknown";

            _sawmill.Debug($"Updated job for player {session.Name} to {workSession.CurrentJob}");
        }
    }

    /// <summary>
    /// Save all current player payment data to persistence
    /// </summary>
    private void SaveAllPlayerPaymentData()
    {
        // Find the persistence entity
        var query = EntityQueryEnumerator<RoundPersistenceComponent>();
        while (query.MoveNext(out var uid, out var persistence))
        {
            persistence.PlayerPayments.Clear();

            // Save data from active sessions
            foreach (var (session, workSession) in _activeSessions)
            {
                var playerId = session.UserId.ToString();
                var currentTime = DateTime.UtcNow;
                var sessionDuration = currentTime - workSession.SessionStartTime;

                // Load existing data if any
                if (persistence.PlayerPayments.TryGetValue(playerId, out var existingData))
                {
                    existingData.TotalHoursWorked += (float)sessionDuration.TotalHours;
                    existingData.CurrentJob = workSession.CurrentJob;
                    existingData.LastJobChange = workSession.LastJobChange;
                    existingData.IsActive = true;
                }
                else
                {
                    persistence.PlayerPayments[playerId] = new PersistedPlayerPayment
                    {
                        PlayerName = session.Name,
                        UserId = playerId,
                        CurrentJob = workSession.CurrentJob,
                        TotalHoursWorked = (float)sessionDuration.TotalHours,
                        AccumulatedPay = 0, // Will be calculated based on hours and job
                        LastPayment = DateTime.UtcNow,
                        LastJobChange = workSession.LastJobChange,
                        IsActive = true,
                        LastStationAssociation = GetPlayerStationAssociation(session)
                    };
                }
            }

            _sawmill.Info($"Saved payment data for {persistence.PlayerPayments.Count} players");
            return;
        }
    }

    /// <summary>
    /// Restore player payment data from persistence
    /// </summary>
    private void RestorePlayerPaymentData()
    {
        var query = EntityQueryEnumerator<RoundPersistenceComponent>();
        while (query.MoveNext(out var uid, out var persistence))
        {
            _sawmill.Info($"Restored payment data for {persistence.PlayerPayments.Count} players");

            // Restore active sessions for currently connected players
            foreach (var session in _playerManager.Sessions)
            {
                var playerId = session.UserId.ToString();
                if (persistence.PlayerPayments.TryGetValue(playerId, out var paymentData))
                {
                    StartPlayerSession(session);
                    if (_activeSessions.TryGetValue(session, out var workSession))
                    {
                        workSession.CurrentJob = paymentData.CurrentJob;
                        workSession.LastJobChange = paymentData.LastJobChange;
                    }
                }
            }
            return;
        }
    }

    /// <summary>
    /// Get the station association for a player
    /// </summary>
    private string GetPlayerStationAssociation(ICommonSession session)
    {
        if (session.AttachedEntity == null)
            return "Unknown";

        var station = _station.GetOwningStation(session.AttachedEntity.Value);
        if (station != null && EntityManager.EntityExists(station.Value) && !TerminatingOrDeleted(station.Value))
        {
            if (EntityManager.TryGetComponent<MetaDataComponent>(station.Value, out var stationMeta))
            {
                return stationMeta.EntityName;
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Get accumulated payment data for a player
    /// </summary>
    public PersistedPlayerPayment? GetPlayerPaymentData(string userId)
    {
        var query = EntityQueryEnumerator<RoundPersistenceComponent>();
        while (query.MoveNext(out var uid, out var persistence))
        {
            return persistence.PlayerPayments.TryGetValue(userId, out var data) ? data : null;
        }
        return null;
    }

    /// <summary>
    /// Update accumulated pay for a player
    /// </summary>
    public void UpdatePlayerPay(string userId, int payAmount)
    {
        var query = EntityQueryEnumerator<RoundPersistenceComponent>();
        while (query.MoveNext(out var uid, out var persistence))
        {
            if (persistence.PlayerPayments.TryGetValue(userId, out var data))
            {
                data.AccumulatedPay += payAmount;
                data.LastPayment = DateTime.UtcNow;
            }
            return;
        }
    }

    /// <summary>
    /// Class to track a player's work session
    /// </summary>
    private sealed class PlayerWorkSession
    {
        public string PlayerName = string.Empty;
        public string UserId = string.Empty;
        public DateTime SessionStartTime;
        public DateTime? SessionEndTime;
        public string CurrentJob = "Unknown";
        public DateTime LastJobChange;
        public float HoursWorked;
    }
}
