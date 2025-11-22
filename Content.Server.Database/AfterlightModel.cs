using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Content.Shared.Database._Afterlight;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbContext
{
    public DbSet<ALKinks> Kinks { get; set; } = null!;
    public DbSet<ALVoreSpaces> VoreSpaces { get; set; } = null!;
    public DbSet<ALContentPreferences> ContentPreferences { get; set; } = null!;
}

public sealed class AfterlightModel : DataModelBase
{
    public override void OnModelCreating(ServerDbContext dbContext, ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ALKinks>()
            .HasOne(k => k.Player)
            .WithMany(p => p.Kinks)
            .HasForeignKey(k => k.PlayerId)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ALVoreSpaces>()
            .HasOne(s => s.Player)
            .WithMany(p => p.VoreSpaces)
            .HasForeignKey(s => s.PlayerId)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ALContentPreferences>()
            .HasOne(s => s.Player)
            .WithMany(p => p.InteractionContentPreferences)
            .HasForeignKey(s => s.PlayerId)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

[Table("al_kinks")]
[PrimaryKey(nameof(PlayerId), nameof(KinkId))]
public sealed class ALKinks
{
    [Key]
    [ForeignKey(nameof(Player))]
    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    [Key]
    public string KinkId { get; set; } = null!;

    public KinkPreference Preference { get; set; }
}

[Table("al_vore_spaces")]
[Index(nameof(PlayerId))]
public sealed class ALVoreSpaces
{
    [Key]
    public Guid SpaceId { get; set; }

    [ForeignKey(nameof(Player))]
    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Overlay { get; set; }
    public VoreSpaceMode Mode { get; set; }
    public double BurnDamage { get; set; }
    public double BruteDamage { get; set; }
    public bool MuffleRadio { get; set; }
    public int ChanceToEscape { get; set; }
    public TimeSpan TimeToEscape { get; set; }
    public bool CanTaste { get; set; }
    public string? InsertionVerb { get; set; }
    public string? ReleaseVerb { get; set; }
    // public bool FancySounds { get; set; }
    public bool Fleshy { get; set; }
    public bool InternalSoundLoop { get; set; }
    public string? InsertionSound { get; set; }
    public string? ReleaseSound { get; set; }
    // public List<string> DigestMessagesOwner { get; set; } = null!;
    // public List<string> DigestMessagesPrey { get; set; } = null!;
    // public List<string> AbsorbMessagesOwner { get; set; } = null!;
    // public List<string> AbsorbMessagesPrey { get; set; } = null!;
    // public List<string> UnabsorbMessagesOwner { get; set; } = null!;
    // public List<string> UnabsorbMessagesPrey { get; set; } = null!;
    // public List<string> StruggleMessagesOutside { get; set; } = null!;
    // public List<string> StruggleMessagesInside { get; set; } = null!;
    // public List<string> AbsorbedStruggleMessagesOutside { get; set; } = null!;
    // public List<string> AbsorbedStruggleMessagesInside { get; set; } = null!;
    // public List<string> EscapeAttemptMessagesOwner { get; set; } = null!;
    // public List<string> EscapeAttemptMessagesPrey { get; set; } = null!;
    // public List<string> EscapeMessagesOwner { get; set; } = null!;
    // public List<string> EscapeMessagesPrey { get; set; } = null!;
    // public List<string> EscapeMessagesOutside { get; set; } = null!;
    // public List<string> EscapeFailMessagesOwner { get; set; } = null!;
    // public List<string> EscapeFailMessagesPrey { get; set; } = null!;
}

[Table("al_content_preferences")]
[Index(nameof(PlayerId))]
[PrimaryKey(nameof(PlayerId), nameof(PreferenceId))]
public sealed class ALContentPreferences
{
    [Key]
    [ForeignKey(nameof(Player))]
    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    [Key]
    public string PreferenceId { get; set; } = null!;

    public bool Value;
}
