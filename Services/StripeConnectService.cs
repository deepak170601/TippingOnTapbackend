using Stripe;
using StripeTerminalBackend.Data;

namespace StripeTerminalBackend.Services;

public class StripeConnectService
{
    private readonly AppDbContext _db;
    private readonly ILogger<StripeConnectService> _logger;

    public StripeConnectService(
        AppDbContext db,
        IConfiguration config,
        ILogger<StripeConnectService> logger)
    {
        _db = db;
        _logger = logger;

        // Stripe key already set globally in Program.cs via StripeConfiguration.ApiKey
        // No need to re-set it here
    }

    // ── Method 1 — CreateConnectedAccountAsync ────────────────
    // Creates a Stripe Express account for the user.
    // Idempotent — if account already exists, returns existing ID.
    public async Task<string> CreateConnectedAccountAsync(string userId)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        // Idempotent — return existing account if already created
        if (!string.IsNullOrEmpty(user.StripeAccountId))
        {
            _logger.LogInformation(
                "User {UserId} already has connected account {AccountId}.",
                userId, user.StripeAccountId);
            return user.StripeAccountId;
        }

        try
        {
            var service = new AccountService();
            var account = await service.CreateAsync(new AccountCreateOptions
            {
                Type = "express",
                Country = "US",
                Email = user.Email,
                Capabilities = new AccountCapabilitiesOptions
                {
                    CardPayments = new AccountCapabilitiesCardPaymentsOptions
                    { Requested = true },
                    Transfers = new AccountCapabilitiesTransfersOptions
                    { Requested = true },
                },
            });

            user.StripeAccountId = account.Id;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Created connected account {AccountId} for user {UserId}.",
                account.Id, userId);

            return account.Id;
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                "Stripe error creating account for user {UserId}: {Message}",
                userId, ex.Message);
            throw;
        }
    }

    // ── Method 2 — GenerateOnboardingLinkAsync ────────────────
    // Returns a single-use Stripe-hosted onboarding URL (expires in 10 min).
    // Never cache this URL.
    public async Task<string> GenerateOnboardingLinkAsync(
        string userId,
        string returnUrl,
        string refreshUrl)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (string.IsNullOrEmpty(user.StripeAccountId))
            throw new InvalidOperationException(
                "Connected account not created. Call CreateConnectedAccountAsync first.");

        try
        {
            var service = new AccountLinkService();
            var link = await service.CreateAsync(new AccountLinkCreateOptions
            {
                Account = user.StripeAccountId,
                ReturnUrl = returnUrl,
                RefreshUrl = refreshUrl,
                Type = "account_onboarding",
            });

            _logger.LogInformation(
                "Generated onboarding link for account {AccountId}.",
                user.StripeAccountId);

            return link.Url;
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                "Stripe error generating onboarding link for user {UserId}: {Message}",
                userId, ex.Message);
            throw;
        }
    }

    // ── Method 3 — GetAccountStatusAsync ─────────────────────
    // Fetches live status from Stripe and syncs to DB.
    // Always call this instead of reading DB flags directly.
    public async Task<(bool chargesEnabled, bool payoutsEnabled)> GetAccountStatusAsync(
        string userId)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        // No account yet — return false immediately
        if (string.IsNullOrEmpty(user.StripeAccountId))
            return (false, false);

        try
        {
            var service = new AccountService();
            var account = await service.GetAsync(user.StripeAccountId);

            // Sync live values to DB
            user.ChargesEnabled = account.ChargesEnabled;
            user.PayoutsEnabled = account.PayoutsEnabled;

            if (account.ChargesEnabled && account.PayoutsEnabled)
                user.OnboardingComplete = true;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Account {AccountId} status — charges: {Charges}, payouts: {Payouts}.",
                user.StripeAccountId, account.ChargesEnabled, account.PayoutsEnabled);

            return (account.ChargesEnabled, account.PayoutsEnabled);
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                "Stripe error fetching account status for user {UserId}: {Message}",
                userId, ex.Message);
            throw;
        }
    }

    // ── Method 4 — GetConnectedBalanceAsync ───────────────────
    // Returns the connected account's balance in cents.
    // CRITICAL: RequestOptions.StripeAccount is mandatory —
    //           without it you get the PLATFORM balance, not the user's.
    public async Task<(long availableCents, long pendingCents)> GetConnectedBalanceAsync(
        string userId)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (string.IsNullOrEmpty(user.StripeAccountId))
            throw new InvalidOperationException(
                "User has no connected account.");

        try
        {
            var requestOptions = new RequestOptions
            {
                StripeAccount = user.StripeAccountId,
            };

            var service = new BalanceService();
            var balance = await service.GetAsync(requestOptions);

            var available = balance.Available
                .FirstOrDefault(b => b.Currency == "usd")?.Amount ?? 0;
            var pending = balance.Pending
                .FirstOrDefault(b => b.Currency == "usd")?.Amount ?? 0;

            _logger.LogInformation(
                "Account {AccountId} balance — available: {Available}, pending: {Pending}.",
                user.StripeAccountId, available, pending);

            return (available, pending);
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                "Stripe error fetching balance for user {UserId}: {Message}",
                userId, ex.Message);
            throw;
        }
    }

    // ── Method 5 — CreatePayoutAsync ─────────────────────────
    // Initiates a payout to the user's linked bank account.
    // CRITICAL: RequestOptions.StripeAccount is mandatory here too.
    // Pass amountCents = null to pay out full available balance.
    public async Task<string> CreatePayoutAsync(string userId, long? amountCents)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        if (string.IsNullOrEmpty(user.StripeAccountId))
            throw new InvalidOperationException("User has no connected account.");

        // Resolve amount — use available balance if not specified
        long resolvedAmount;
        if (amountCents is null)
        {
            var (available, _) = await GetConnectedBalanceAsync(userId);
            resolvedAmount = available;
        }
        else
        {
            resolvedAmount = amountCents.Value;
        }

        if (resolvedAmount <= 0)
            throw new InvalidOperationException("No available balance to withdraw.");

        try
        {
            var requestOptions = new RequestOptions
            {
                StripeAccount = user.StripeAccountId,
            };

            var service = new PayoutService();
            var payout = await service.CreateAsync(
                new PayoutCreateOptions
                {
                    Amount = resolvedAmount,
                    Currency = "usd",
                },
                requestOptions
            );

            _logger.LogInformation(
                "Payout {PayoutId} of {Amount} cents created for account {AccountId}.",
                payout.Id, resolvedAmount, user.StripeAccountId);

            return payout.Id;
        }
        catch (StripeException ex)
        {
            _logger.LogError(
                "Stripe error creating payout for user {UserId}: {Message}",
                userId, ex.Message);
            throw;
        }
    }
}