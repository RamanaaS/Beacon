using Beacon.Domain.Incidents;
using Microsoft.EntityFrameworkCore;

namespace Beacon.Infrastructure.Incidents;

public sealed class BeaconDbContext(DbContextOptions<BeaconDbContext> options) : DbContext(options)
{
    public DbSet<Incident> Incidents => Set<Incident>();

    /// <summary>
    /// Deliberately not split into per-aggregate IEntityTypeConfiguration classes yet — with a
    /// single aggregate root, that indirection buys nothing. Split it out once a second
    /// aggregate exists and this method starts doing two unrelated things.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.ToTable("Incidents");

            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id)
                .HasConversion(id => id.Value, value => new IncidentId(value))
                .ValueGeneratedNever();

            // These are projections computed from _events at read time, not stored columns.
            entity.Ignore(i => i.Events);
            entity.Ignore(i => i.Title);
            entity.Ignore(i => i.CurrentSeverity);
            entity.Ignore(i => i.CurrentStatus);
            entity.Ignore(i => i.Responders);

            // Map straight to the private backing field — Incident exposes no public setter
            // for its event log, and none should exist. See ARCHITECTURE.md Section 4 and
            // LEARNING_GUIDE.md Phase 1 for why.
            entity.HasMany<IncidentEvent>("_events")
                .WithOne()
                .HasForeignKey("IncidentId")
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation("_events")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<IncidentEvent>(entity =>
        {
            entity.ToTable("IncidentEvents");

            entity.Property<int>("Id");
            entity.HasKey("Id");

            // Must be typed IncidentId (matching the principal key's CLR type), not the raw
            // Guid it converts to — EF Core requires FK/PK CLR types to match before conversion.
            entity.Property<IncidentId>("IncidentId")
                .HasConversion(id => id.Value, value => new IncidentId(value));

            entity.Property(e => e.OccurredAt).IsRequired();

            // Table-Per-Hierarchy: one table, one nullable-column-per-event-specific-field,
            // discriminated by EventType. Simplest option that keeps LINQ queryability over
            // the whole event log. The alternative — one JSON column holding the event-specific
            // payload — trades that queryability for schema flexibility (no migration needed to
            // add a new event type's fields) and is what a system with dozens of event types, or
            // that changes shape often, would reach for instead. Six event types with stable
            // shapes doesn't clear that bar yet.
            entity.HasDiscriminator<string>("EventType")
                .HasValue<IncidentDeclared>("Declared")
                .HasValue<ResponderJoined>("ResponderJoined")
                .HasValue<NoteAdded>("NoteAdded")
                .HasValue<SeverityChanged>("SeverityChanged")
                .HasValue<IncidentAcknowledged>("Acknowledged")
                .HasValue<IncidentResolved>("Resolved")
                .HasValue<IncidentEscalated>("Escalated");
        });

        // Every event type has a "who did this" field, but under different property names
        // (DeclaredByUserId vs. UserId) because that's what reads naturally in the domain
        // model. Left unconfigured, TPH gives each one its own nullable column per type
        // (IncidentEvents.UserId, .NoteAdded_UserId, .ResponderJoined_UserId, ...) — technically
        // correct but a sprawl of near-duplicate columns for one underlying concept. Pointing
        // them at the same physical column keeps the table honest about what's actually one
        // idea wearing different domain-level names.
        const string performedByColumn = "PerformedByUserId";

        modelBuilder.Entity<IncidentDeclared>(entity =>
        {
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DeclaredByUserId).HasColumnName(performedByColumn).IsRequired();
            entity.Property(e => e.AlertFingerprint).IsRequired();
        });

        modelBuilder.Entity<ResponderJoined>(entity =>
        {
            entity.Property(e => e.UserId).HasColumnName(performedByColumn).IsRequired();
            entity.Property(e => e.Role).IsRequired();
        });

        modelBuilder.Entity<NoteAdded>(entity =>
        {
            entity.Property(e => e.UserId).HasColumnName(performedByColumn).IsRequired();
            entity.Property(e => e.Text).IsRequired();
        });

        modelBuilder.Entity<SeverityChanged>(entity =>
        {
            entity.Property(e => e.UserId).HasColumnName(performedByColumn).IsRequired();
            entity.Property(e => e.OldSeverity).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.NewSeverity).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<IncidentAcknowledged>(entity =>
        {
            entity.Property(e => e.UserId).HasColumnName(performedByColumn).IsRequired();
        });

        modelBuilder.Entity<IncidentResolved>(entity =>
        {
            entity.Property(e => e.UserId).HasColumnName(performedByColumn).IsRequired();
            entity.Property(e => e.Summary).IsRequired();
        });

        modelBuilder.Entity<IncidentEscalated>(entity =>
        {
            entity.Property(e => e.EscalatedByUserId).HasColumnName(performedByColumn).IsRequired();
            entity.Property(e => e.EscalatedTo).IsRequired();
        });
    }
}
