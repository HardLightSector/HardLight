using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Afterlight.Kinks;
using Content.Shared._Afterlight.MobInteraction;
using Content.Shared._Afterlight.Vore;
using Content.Shared.Database._Afterlight;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

// ReSharper disable CheckNamespace

namespace Content.Server.Database;

public partial class ServerDbBase
{
    #region Kinks

    public async Task<List<ALKinks>> GetKinks(Guid player, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.Kinks.Where(k => k.PlayerId == player).ToListAsync(cancel);
    }

    public async Task SetKink(Guid player,
        EntProtoId<KinkDefinitionComponent> kinkId,
        KinkPreference preference,
        CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var kink = await db.DbContext.Kinks.FirstOrDefaultAsync(k => k.PlayerId == player && k.KinkId == kinkId.Id,
            cancel);
        kink ??= db.DbContext.Kinks.Add(new ALKinks
            {
                PlayerId = player,
                KinkId = kinkId,
            })
            .Entity;

        kink.Preference = preference;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task UpdateKinks(Guid player,
        Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks,
        CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        foreach (var (kinkId, preference) in kinks)
        {
            var kink = await db.DbContext.Kinks.FirstOrDefaultAsync(k => k.PlayerId == player && k.KinkId == kinkId.Id,
                cancel);
            kink ??= db.DbContext.Kinks.Add(new ALKinks
                {
                    PlayerId = player,
                    KinkId = kinkId
                })
                .Entity;

            kink.Preference = preference;
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task UpdateKinks(Guid player,
        IEnumerable<EntProtoId<KinkDefinitionComponent>> kinks,
        KinkPreference preference,
        CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        foreach (var kinkId in kinks)
        {
            var kink = await db.DbContext.Kinks.FirstOrDefaultAsync(k => k.PlayerId == player && k.KinkId == kinkId.Id,
                cancel);
            kink ??= db.DbContext.Kinks.Add(new ALKinks
                {
                    PlayerId = player,
                    KinkId = kinkId
                })
                .Entity;

            kink.Preference = preference;
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task RemoveKinks(Guid player, EntProtoId<KinkDefinitionComponent> kinkId, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var kink = await db.DbContext.Kinks.FirstOrDefaultAsync(k => k.PlayerId == player && k.KinkId == kinkId.Id,
            cancel);
        if (kink == null)
            return;

        db.DbContext.Kinks.Remove(kink);

        await db.DbContext.SaveChangesAsync(cancel);
    }

    #endregion

    #region Vore

    public async Task<List<VoreSpace>> GetVoreSpaces(Guid player, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbSpaces = await db.DbContext.VoreSpaces
            .Where(s => s.PlayerId == player)
            .ToListAsync(cancel);

        var spaces = new List<VoreSpace>(dbSpaces.Count);
        foreach (var s in dbSpaces)
        {
            // TODO AFTERLIGHT
            // var messages = new Dictionary<VoreMessageType, List<string>>();
            // messages[VoreMessageType.DigestOwner] = s.DigestMessagesOwner;
            // messages[VoreMessageType.DigestPrey] = s.DigestMessagesPrey;
            // messages[VoreMessageType.AbsorbOwner] = s.AbsorbMessagesOwner;
            // messages[VoreMessageType.AbsorbPrey] = s.AbsorbMessagesPrey;
            // messages[VoreMessageType.UnabsorbOwner] = s.UnabsorbMessagesOwner;
            // messages[VoreMessageType.UnabsorbPrey] = s.UnabsorbMessagesPrey;
            // messages[VoreMessageType.StruggleOutside] = s.StruggleMessagesOutside;
            // messages[VoreMessageType.StruggleInside] = s.StruggleMessagesInside;
            // messages[VoreMessageType.AbsorbedStruggleOutside] = s.AbsorbedStruggleMessagesOutside;
            // messages[VoreMessageType.AbsorbedStruggleInside] = s.AbsorbedStruggleMessagesInside;
            // messages[VoreMessageType.EscapeAttemptOwner] = s.EscapeAttemptMessagesOwner;
            // messages[VoreMessageType.EscapeAttemptPrey] = s.EscapeAttemptMessagesPrey;
            // messages[VoreMessageType.EscapeOwner] = s.EscapeMessagesOwner;
            // messages[VoreMessageType.EscapePrey] = s.EscapeMessagesPrey;
            // messages[VoreMessageType.EscapeOutside] = s.EscapeMessagesOutside;
            // messages[VoreMessageType.EscapeFailOwner] = s.EscapeFailMessagesOwner;
            // messages[VoreMessageType.EscapeFailPrey] = s.EscapeFailMessagesPrey;

            spaces.Add(new VoreSpace(
                s.SpaceId,
                s.Name,
                s.Description,
                s.Overlay,
                s.Mode,
                s.BurnDamage,
                s.BruteDamage,
                s.MuffleRadio,
                s.ChanceToEscape,
                s.TimeToEscape,
                s.CanTaste,
                s.InsertionVerb,
                s.ReleaseVerb,
                s.Fleshy,
                s.InternalSoundLoop,
                s.InsertionSound == null ? null : new SoundPathSpecifier(s.InsertionSound),
                s.ReleaseSound == null ? null : new SoundPathSpecifier(s.ReleaseSound),
                new Dictionary<VoreMessageType, List<string>>() // TODO AFTERLIGHT
            ));
        }

        return spaces;
    }

    public async Task UpdateVoreSpace(Guid player, VoreSpace space, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var spaceModel = await db.DbContext.VoreSpaces.FirstOrDefaultAsync(s => s.PlayerId == player && s.SpaceId == space.Id, cancel);
        spaceModel ??= db.DbContext.VoreSpaces.Add(new ALVoreSpaces
        {
            PlayerId = player,
            SpaceId = space.Id,
        }).Entity;

        spaceModel.Name = space.Name;
        spaceModel.Description = space.Description;
        spaceModel.Overlay = space.Overlay;
        spaceModel.Mode = space.Mode;
        spaceModel.BurnDamage = space.BurnDamage.Double();
        spaceModel.BruteDamage = space.BruteDamage.Double();
        spaceModel.MuffleRadio = space.MuffleRadio;
        spaceModel.ChanceToEscape = space.ChanceToEscape;
        spaceModel.TimeToEscape = space.TimeToEscape;
        spaceModel.CanTaste = space.CanTaste;
        spaceModel.InsertionVerb = space.InsertionVerb;
        spaceModel.ReleaseVerb = space.ReleaseVerb;
        // spaceModel.FancySounds = space.FancySounds;
        spaceModel.Fleshy = space.Fleshy;
        spaceModel.InternalSoundLoop = space.InternalSoundLoop;

        // TODO AFTERLIGHT
        // spaceModel.DigestMessagesOwner = space.Messages.GetValueOrDefault(VoreMessageType.DigestOwner) ?? new List<string>();
        // spaceModel.DigestMessagesPrey = space.Messages.GetValueOrDefault(VoreMessageType.DigestPrey) ?? new List<string>();
        // spaceModel.AbsorbMessagesOwner = space.Messages.GetValueOrDefault(VoreMessageType.AbsorbOwner) ?? new List<string>();
        // spaceModel.AbsorbMessagesPrey = space.Messages.GetValueOrDefault(VoreMessageType.AbsorbPrey) ?? new List<string>();
        // spaceModel.UnabsorbMessagesOwner = space.Messages.GetValueOrDefault(VoreMessageType.UnabsorbOwner) ?? new List<string>();
        // spaceModel.UnabsorbMessagesPrey = space.Messages.GetValueOrDefault(VoreMessageType.UnabsorbPrey) ?? new List<string>();
        // spaceModel.StruggleMessagesOutside = space.Messages.GetValueOrDefault(VoreMessageType.StruggleOutside) ?? new List<string>();
        // spaceModel.StruggleMessagesInside = space.Messages.GetValueOrDefault(VoreMessageType.StruggleInside) ?? new List<string>();
        // spaceModel.AbsorbedStruggleMessagesOutside = space.Messages.GetValueOrDefault(VoreMessageType.AbsorbedStruggleOutside) ?? new List<string>();
        // spaceModel.AbsorbedStruggleMessagesInside = space.Messages.GetValueOrDefault(VoreMessageType.AbsorbedStruggleInside) ?? new List<string>();
        // spaceModel.EscapeAttemptMessagesOwner = space.Messages.GetValueOrDefault(VoreMessageType.EscapeAttemptOwner) ?? new List<string>();
        // spaceModel.EscapeAttemptMessagesPrey = space.Messages.GetValueOrDefault(VoreMessageType.EscapeAttemptPrey) ?? new List<string>();
        // spaceModel.EscapeMessagesOwner = space.Messages.GetValueOrDefault(VoreMessageType.EscapeOwner) ?? new List<string>();
        // spaceModel.EscapeMessagesPrey = space.Messages.GetValueOrDefault(VoreMessageType.EscapePrey) ?? new List<string>();
        // spaceModel.EscapeMessagesOutside = space.Messages.GetValueOrDefault(VoreMessageType.EscapeOutside) ?? new List<string>();
        // spaceModel.EscapeFailMessagesOwner = space.Messages.GetValueOrDefault(VoreMessageType.EscapeFailOwner) ?? new List<string>();
        // spaceModel.EscapeFailMessagesPrey = space.Messages.GetValueOrDefault(VoreMessageType.EscapeFailPrey) ?? new List<string>();

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DeleteVoreSpace(Guid player, Guid space, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbSpace = await db.DbContext.VoreSpaces
            .SingleOrDefaultAsync(s => s.PlayerId == player && s.SpaceId == space, cancel);

        if (dbSpace == null)
            return;

        db.DbContext.VoreSpaces.Remove(dbSpace);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    #endregion

    #region Interaction Preferences

    public async Task InitContentPreferences(Guid player, HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        if (await db.DbContext.ContentPreferences.AnyAsync(p => p.PlayerId == player, cancel))
            return;

        foreach (var preference in preferences)
        {
            db.DbContext.ContentPreferences.Add(new ALContentPreferences
            {
                PlayerId = player,
                PreferenceId = preference,
                Value = true,
            });
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<HashSet<EntProtoId<ALContentPreferenceComponent>>> GetContentPreferences(Guid player, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var preferences = await db.DbContext.ContentPreferences
            .Where(p => p.PlayerId == player)
            .ToHashSetAsync(cancel);

        return preferences
            .Select(p => new EntProtoId<ALContentPreferenceComponent>(p.PreferenceId))
            .ToHashSet();
    }

    public async Task SetContentPreferences(Guid player, HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbPreferences = await db.DbContext.ContentPreferences
            .Where(p => p.PlayerId == player)
            .ToHashSetAsync(cancel);

        var toAdd = preferences.ToHashSet();
        foreach (var preference in dbPreferences)
        {
            if (toAdd.Contains(preference.PreferenceId))
            {
                toAdd.Remove(preference.PreferenceId);
                continue;
            }

            db.DbContext.Remove(preference);
        }

        foreach (var preference in toAdd)
        {
            db.DbContext.ContentPreferences.Add(new ALContentPreferences
            {
                PlayerId = player,
                PreferenceId = preference,
            });
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DisableContentPreference(Guid player, string preference, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbPreference = await db.DbContext.ContentPreferences
            .SingleOrDefaultAsync(p => p.PlayerId == player && p.PreferenceId == preference, cancel);

        dbPreference ??= db.DbContext.ContentPreferences.Add(new ALContentPreferences
            {
                PlayerId = player,
                PreferenceId = preference
            })
            .Entity;
        dbPreference.Value = false;

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task EnableContentPreference(Guid player, string preference, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbPreference = await db.DbContext.ContentPreferences
            .SingleOrDefaultAsync(p => p.PlayerId == player && p.PreferenceId == preference, cancel);

        dbPreference ??= db.DbContext.ContentPreferences.Add(new ALContentPreferences
            {
                PlayerId = player,
                PreferenceId = preference
            })
            .Entity;
        dbPreference.Value = true;

        await db.DbContext.SaveChangesAsync(cancel);
    }

    #endregion
}
