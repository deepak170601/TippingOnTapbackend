using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StripeTerminalBackend.Models;

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // ── Name ──────────────────────────────────────────────────
    [Required]
    [Column("first_name")]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Column("last_name")]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Column("full_name")]
    [MaxLength(255)]
    public string FullName => $"{FirstName} {LastName}";

    // ── Contact ───────────────────────────────────────────────
    [Required]
    [Column("phone_number")]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [Column("email")]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Column("is_email_verified")]
    public bool IsEmailVerified { get; set; } = false;

    // ── Business (optional) ───────────────────────────────────
    [Column("company_name")]
    [MaxLength(255)]
    public string? CompanyName { get; set; }

    [Column("ein")]
    [MaxLength(20)]
    public string? Ein { get; set; }

    // ── Address ───────────────────────────────────────────────
    [Column("address1")]
    [MaxLength(255)]
    public string Address1 { get; set; } = string.Empty;

    [Column("address2")]
    [MaxLength(255)]
    public string? Address2 { get; set; }

    [Column("city")]
    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Column("state")]
    [MaxLength(2)]
    public string State { get; set; } = string.Empty;

    [Column("zip")]
    [MaxLength(10)]
    public string Zip { get; set; } = string.Empty;


    // ── Stripe Connect fields — added Goal 1 ──────────────────
    [Column("stripe_account_id")]
    [MaxLength(255)]
    public string? StripeAccountId { get; set; }        // e.g. acct_xxxxx — null until account created

    [Column("onboarding_complete")]
    public bool OnboardingComplete { get; set; } = false; // true when charges + payouts both enabled

    [Column("charges_enabled")]
    public bool ChargesEnabled { get; set; } = false;     // mirrors Stripe account.charges_enabled

    [Column("payouts_enabled")]
    public bool PayoutsEnabled { get; set; } = false;     // mirrors Stripe account.payouts_enabled

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}