using Microsoft.AspNetCore.Identity;

namespace Beacon.Infrastructure.Auth;

/// <summary>
/// Identity's user model is authentication plumbing, not a domain aggregate — it stays in
/// Infrastructure rather than getting DDD treatment in Beacon.Domain. Incident.Declare and every
/// other domain method still just takes a plain userId string; the domain has never needed to
/// know anything about how that identity was authenticated.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
