using Beacon.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace Beacon.Api.Auth;

/// <summary>
/// Same precedent as AuthEndpoints (Phase 7): user/role management talks to UserManager/
/// RoleManager directly rather than through MediatR — it's Identity plumbing, not part of the
/// incident-orchestration bounded context CQRS was scoped to.
/// </summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization("AdminOnly");

        group.MapGet("/users", async (UserManager<ApplicationUser> userManager, CancellationToken ct) =>
            {
                var users = new List<AdminUserResponse>();
                foreach (var user in userManager.Users.ToList())
                {
                    var roles = await userManager.GetRolesAsync(user);
                    users.Add(new AdminUserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName, roles));
                }
                return Results.Ok(users);
            })
            .WithName("ListUsers")
            .Produces<List<AdminUserResponse>>();

        group.MapPut("/users/{id}/role", async (
                string id,
                UpdateUserRoleRequest request,
                UserManager<ApplicationUser> userManager) =>
            {
                if (!Roles.All.Contains(request.Role))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["role"] = [$"Role must be one of: {string.Join(", ", Roles.All)}"]
                    });

                var user = await userManager.FindByIdAsync(id);
                if (user is null) return Results.NotFound();

                var currentRoles = await userManager.GetRolesAsync(user);
                await userManager.RemoveFromRolesAsync(user, currentRoles);
                await userManager.AddToRoleAsync(user, request.Role);

                return Results.NoContent();
            })
            .WithName("UpdateUserRole")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private sealed record UpdateUserRoleRequest(string Role);
}

public sealed record AdminUserResponse(string Id, string Email, string DisplayName, IList<string> Roles);
