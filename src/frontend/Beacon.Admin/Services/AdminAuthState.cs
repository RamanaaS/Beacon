namespace Beacon.Admin.Services;

/// <summary>
/// Registered Scoped — one instance per Blazor Server circuit (one per connected browser tab),
/// which is what makes it safe to hold a JWT in a plain in-memory field: it never crosses
/// between users the way a Singleton would. No browser storage involved, unlike Beacon.WarRoom's
/// localStorage-backed AuthStore — a Blazor Server circuit already lives server-side for the
/// tab's lifetime, so there's nothing to persist across a page load the way an SPA needs to.
/// </summary>
public sealed class AdminAuthState
{
    public string? Token { get; private set; }
    public string? Email { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = [];

    public bool IsAuthenticated => Token is not null;
    public bool IsAdmin => Roles.Contains("Admin");

    public event Action? Changed;

    public void SetSession(string token, string email, IReadOnlyList<string> roles)
    {
        Token = token;
        Email = email;
        Roles = roles;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Token = null;
        Email = null;
        Roles = [];
        Changed?.Invoke();
    }
}
