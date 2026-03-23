using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StripeTerminalBackend.Models;

[Table("tips")]
public class Tip
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    [Required]
    [Column("amount")]
    public long Amount { get; set; } // cents

    [Column("payment_intent_id")]
    [MaxLength(255)]
    public string? PaymentIntentId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public Event Event { get; set; } = null!;
}