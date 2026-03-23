using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StripeTerminalBackend.Models;

public enum OtpType { Phone, Email }

[Table("otp_codes")]
public class OtpCode
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Phone number OR email depending on Type
    [Required]
    [Column("target")]
    [MaxLength(255)]
    public string Target { get; set; } = string.Empty;

    [Required]
    [Column("type")]
    public OtpType Type { get; set; }

    [Required]
    [Column("code")]
    [MaxLength(6)]
    public string Code { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);

    [Column("verified_at")]
    public DateTime? VerifiedAt { get; set; }

    [Column("used_at")]
    public DateTime? UsedAt { get; set; }

    [NotMapped] public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    [NotMapped] public bool IsVerified => VerifiedAt.HasValue;
    [NotMapped] public bool IsUsed => UsedAt.HasValue;
    [NotMapped] public bool IsValid => !IsExpired && !IsUsed;
}