using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Afterlight.Vore;
using Content.Shared.Database._Afterlight;
using Content.Shared._Afterlight.MobInteraction;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Content.Shared.FixedPoint;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    #region Vore

    public async Task<List<VoreSpace>> GetVoreSpaces(Guid player, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var spaces = await db.DbContext.VoreSpaces.Where(s => s.PlayerId == player).ToListAsync(cancel);

        var result = new List<VoreSpace>(spaces.Count);
        foreach (var s in spaces)
        {
            var overlayId = string.IsNullOrEmpty(s.Overlay) ? (EntProtoId<VoreOverlayComponent>?) null : new EntProtoId<VoreOverlayComponent>(s.Overlay);
            var messages = new Dictionary<VoreMessageType, List<string>>();

            var space = new VoreSpace(
                s.SpaceId,
                s.Name,
                s.Description,
                overlayId,
                s.Mode,
                FixedPoint2.New((float) s.BurnDamage),
                FixedPoint2.New((float) s.BruteDamage),
                s.MuffleRadio,
                s.ChanceToEscape,
                s.TimeToEscape,
                s.CanTaste,
                s.InsertionVerb,
                s.ReleaseVerb,
                s.Fleshy,
                s.InternalSoundLoop,
                string.IsNullOrEmpty(s.InsertionSound) ? null : new SoundPathSpecifier(s.InsertionSound),
                string.IsNullOrEmpty(s.ReleaseSound) ? null : new SoundPathSpecifier(s.ReleaseSound),
                messages
            );

            result.Add(space);
        }

        return result;
    }

    public async Task UpdateVoreSpace(Guid player, VoreSpace space, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbSpace = await db.DbContext.VoreSpaces.FirstOrDefaultAsync(s => s.PlayerId == player && s.SpaceId == space.Id,
            cancel);

        dbSpace ??= db.DbContext.VoreSpaces.Add(new ALVoreSpaces
        {
            SpaceId = space.Id,
            PlayerId = player,
        }).Entity;

        dbSpace.Name = space.Name;
        dbSpace.Description = space.Description;
        dbSpace.Overlay = space.Overlay?.Id;
        dbSpace.Mode = space.Mode;
        dbSpace.BurnDamage = space.BurnDamage.Float();
        dbSpace.BruteDamage = space.BruteDamage.Float();
        dbSpace.MuffleRadio = space.MuffleRadio;
        dbSpace.ChanceToEscape = space.ChanceToEscape;
        dbSpace.TimeToEscape = space.TimeToEscape;
        dbSpace.CanTaste = space.CanTaste;
        dbSpace.InsertionVerb = space.InsertionVerb;
        dbSpace.ReleaseVerb = space.ReleaseVerb;
        dbSpace.Fleshy = space.Fleshy;
        dbSpace.InternalSoundLoop = space.InternalSoundLoop;
        dbSpace.InsertionSound = space.InsertionSound == null ? null : space.InsertionSound.Path.ToString();
        dbSpace.ReleaseSound = space.ReleaseSound == null ? null : space.ReleaseSound.Path.ToString();

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DeleteVoreSpace(Guid player, Guid space, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var dbSpace = await db.DbContext.VoreSpaces.FirstOrDefaultAsync(s => s.PlayerId == player && s.SpaceId == space,
            cancel);
        if (dbSpace == null)
            return;

        db.DbContext.VoreSpaces.Remove(dbSpace);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    #endregion

    #region Interaction Preferences

    public async Task InitContentPreferences(Guid player,
        HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var existing = await db.DbContext.ContentPreferences.Where(p => p.PlayerId == player).ToListAsync(cancel);

        foreach (var preference in preferences)
        {
            if (existing.Any(p => p.PreferenceId == preference.Id))
                continue;

            db.DbContext.ContentPreferences.Add(new ALContentPreferences
            {
                PlayerId = player,
                PreferenceId = preference.Id,
                Value = true
            });
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<HashSet<EntProtoId<ALContentPreferenceComponent>>> GetContentPreferences(Guid player, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var preferences = await db.DbContext.ContentPreferences
            .Where(p => p.PlayerId == player && p.Value)
            .Select(p => p.PreferenceId)
            .ToListAsync(cancel);

        return preferences.Select(p => new EntProtoId<ALContentPreferenceComponent>(p)).ToHashSet();
    }

    public async Task SetContentPreferences(Guid player,
        HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var existing = await db.DbContext.ContentPreferences.Where(p => p.PlayerId == player).ToListAsync(cancel);

        // Remove all existing preferences
        db.DbContext.ContentPreferences.RemoveRange(existing);

        // Add new preferences
        foreach (var preference in preferences)
        {
            db.DbContext.ContentPreferences.Add(new ALContentPreferences
            {
                PlayerId = player,
                PreferenceId = preference.Id,
                Value = true
            });
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DisableContentPreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference,
        CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var pref = await db.DbContext.ContentPreferences.FirstOrDefaultAsync(
            p => p.PlayerId == player && p.PreferenceId == preference.Id, cancel);

        if (pref == null)
            return;

        pref.Value = false;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task EnableContentPreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference,
        CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var pref = await db.DbContext.ContentPreferences.FirstOrDefaultAsync(
            p => p.PlayerId == player && p.PreferenceId == preference.Id, cancel);

        pref ??= db.DbContext.ContentPreferences.Add(new ALContentPreferences
        {
            PlayerId = player,
            PreferenceId = preference.Id
        }).Entity;

        pref.Value = true;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    #endregion

    #region Generic DbEntry Handling

    public async Task<bool> Delete<TResult>(Func<ServerDbContext, Task<TResult>> action)
    {
        await using var db = await GetDb();
        var result = await action(db.DbContext);
        if (result == null)
            return false;

        db.DbContext.Remove(result);
        await db.DbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> Delete<T1, TResult>(
        T1 arg1,
        Func<ServerDbContext, T1, Task<TResult>> action)
    {
        await using var db = await GetDb();
        var result = await action(db.DbContext, arg1);
        if (result == null)
            return false;

        db.DbContext.Remove(result);
        await db.DbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> Delete<T1, T2, TResult>(
        T1 arg1,
        T2 arg2,
        Func<ServerDbContext, T1, T2, Task<TResult>> action)
    {
        await using var db = await GetDb();
        var result = await action(db.DbContext, arg1, arg2);
        if (result == null)
            return false;

        db.DbContext.Remove(result);
        await db.DbContext.SaveChangesAsync();
        return true;
    }

    #endregion
}
