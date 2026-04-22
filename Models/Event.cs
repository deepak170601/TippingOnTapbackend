using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StripeTerminalBackend.Models;

public enum EventStatus { Upcoming, Active, Past }

[Table("events")]
public class Event
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("date")]
    public DateOnly Date { get; set; }

    [Column("time")]
    public TimeOnly? Time { get; set; }

    [Column("location")]
    [MaxLength(255)]
    public string Location { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    // Stored as integer[] in Postgres
    [Column("tip_options", TypeName = "integer[]")]
    public int[] TipOptions { get; set; } = new int[0];   // ✅ FIXED

    [Column("status")]
    public EventStatus Status { get; set; } = EventStatus.Upcoming;

    [Column("tips_collected")]
    public int TipsCollected { get; set; } = 0;

    [Column("total_amount")]
    public long TotalAmount { get; set; } = 0;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("ended_at")]
    public DateTime? EndedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<Tip> Tips { get; set; } = new List<Tip>();   // ✅ FIXED
}