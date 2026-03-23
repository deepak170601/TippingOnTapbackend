using Microsoft.EntityFrameworkCore;
using StripeTerminalBackend.Models;

namespace StripeTerminalBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Tip> Tips => Set<Tip>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── User ──────────────────────────────────────────────
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.PhoneNumber)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Ignore(u => u.FullName);

        // ── RefreshToken ──────────────────────────────────────
        modelBuilder.Entity<RefreshToken>()
            .HasOne(rt => rt.User)
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.Token)
            .IsUnique();

        // ── Event ─────────────────────────────────────────────
        modelBuilder.Entity<Event>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Event>()
            .Property(e => e.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Event>()
            .HasIndex(e => new { e.UserId, e.Status });

        // ── Tip ───────────────────────────────────────────────
        modelBuilder.Entity<Tip>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tip>()
            .HasOne(t => t.Event)
            .WithMany(e => e.Tips)
            .HasForeignKey(t => t.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tip>().HasIndex(t => t.UserId);
        modelBuilder.Entity<Tip>().HasIndex(t => t.EventId);

        // ── OtpCode ───────────────────────────────────────────
        modelBuilder.Entity<OtpCode>()
            .HasIndex(o => new { o.Target, o.Type });

        modelBuilder.Entity<OtpCode>()
            .Property(o => o.Type)
            .HasConversion<string>();
    }
}