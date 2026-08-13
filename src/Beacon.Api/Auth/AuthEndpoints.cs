using Beacon.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace Beacon.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (
                RegisterRequest request,
                UserManager<ApplicationUser> userManager,
                CancellationToken ct) =>
            {
                if (!Roles.All.Contains(request.Role))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["role"] = [$"Role must be one of: {string.Join(", ", Roles.All)}"]
                    });

                var user = new ApplicationUser
                {
                    UserName = request.Email,
                    Email = request.Email,
                    DisplayName = request.DisplayName
                };

                var result = await userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                    return Results.ValidationProblem(
                        result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

                await userManager.AddToRoleAsync(user, request.Role);

                return Results.Created($"/api/auth/users/{user.Id}", new { user.Id, user.Email, request.Role });
            })
            .WithName("Register")
            .WithSummary("Register a new user. Letting signup pick its own role (including Responder/Admin) is a " +
                         "demo simplification — a real system would gate elevated roles behind an approval step, " +
                         "not grant them directly at registration.")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/login", async (
                LoginRequest request,
                UserManager<ApplicationUser> userManager,
                SignInManager<ApplicationUser> signInManager,
                IJwtTokenGenerator tokenGenerator,
                CancellationToken ct) =>
            {
                var user = await userManager.FindByEmailAsync(request.Email);
                if (user is null)
                    return Results.Unauthorized();

                // CheckPasswordSignInAsync (not a bare password comparison) so failed attempts
                // count toward the lockout policy configured in AddInfrastructure — five failed
                // attempts locks the account for 15 minutes, real brute-force protection.
                var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
                if (!result.Succeeded)
                    return Results.Unauthorized();

                var roles = await userManager.GetRolesAsync(user);
                var (token, expiresAt) = tokenGenerator.GenerateToken(user.Id, user.Email!, roles);

                return Results.Ok(new LoginResponse(token, expiresAt, roles));
            })
            .WithName("Login")
            .Produces<LoginResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return group;
    }

    private sealed record RegisterRequest(string Email, string Password, string DisplayName, string Role);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, IList<string> Roles);
}
