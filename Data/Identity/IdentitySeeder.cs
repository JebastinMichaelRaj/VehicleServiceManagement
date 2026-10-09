using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using VehicleServiceManagement.Domain.Identity;

namespace VehicleServiceManagement.Data.Identity;

/// <summary>
/// Creates the Admin, Technician and Customer roles and optional starter accounts.
/// Accounts are only created when a password is configured (use user-secrets, never commit passwords).
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole = "Admin";
    public const string TechnicianRole = "Technician";
    public const string CustomerRole = "Customer";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { AdminRole, TechnicianRole, CustomerRole })
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }

        await EnsureUserAsync(users, configuration["Seed:AdminEmail"], configuration["Seed:AdminPassword"], AdminRole, null, logger);
        await EnsureUserAsync(users, configuration["Seed:TechnicianEmail"], configuration["Seed:TechnicianPassword"], TechnicianRole, null, logger);
        // Links the demo customer login to seeded customer #1 so the portal can be exercised immediately.
        await EnsureUserAsync(users, configuration["Seed:CustomerEmail"], configuration["Seed:CustomerPassword"], CustomerRole, 1, logger);
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users,
        string? email,
        string? password,
        string role,
        int? customerId,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed account for role {Role} not configured; skipping.", role);
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CustomerId = customerId };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                logger.LogError("Could not create seed account {Email}: {Errors}", email,
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }
        }

        if (!await users.IsInRoleAsync(user, role))
            await users.AddToRoleAsync(user, role);
    }
}
