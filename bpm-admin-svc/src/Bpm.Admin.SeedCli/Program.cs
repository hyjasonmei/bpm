using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
var allowOverride = Environment.GetEnvironmentVariable("FLOWCOOK_ALLOW_SEED") == "1";
if (env != "Development" && !allowOverride)
{
    Console.Error.WriteLine($"SeedCli refusing to run in environment '{env}'. " +
        "Set ASPNETCORE_ENVIRONMENT=Development or FLOWCOOK_ALLOW_SEED=1 to override.");
    return 2;
}

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "FLOWCOOK_")
    .AddCommandLine(args)
    .Build();

// Default to the shared bpm db so admin + bpm + SeedCli all land on
// <repoRoot>/db/bpm.db (per DbPathResolver). Honour the legacy "Admin"
// key for explicit override.
var connectionString = DbPathResolver.Normalize(
    config.GetConnectionString("Admin")
    ?? config.GetConnectionString("Default")
    ?? "Data Source=bpm.db");

var sub = args.Length > 0 ? args[0] : "help";
var includeOrg = args.Any(a => a == "--org");

switch (sub)
{
    case "clear":
        await Seeder.ClearOrgAsync(connectionString);
        Console.WriteLine("Admin-owned tables truncated (Principals / Roles / Flows / etc).");
        Console.WriteLine("bpm-owned tables in the shared db file are LEFT IN PLACE — run bpm-svc SeedCli to reset those.");
        break;

    case "seed":
        await Seeder.ClearOrgAsync(connectionString);
        Console.WriteLine("Admin DB recreated.");
        if (includeOrg)
        {
            await Seeder.SeedOrgAsync(connectionString);
            Console.WriteLine("Org data seeded (~13 users / ~6 depts / 1 group / ~14 roles).");
        }
        else
        {
            Console.WriteLine("No --org flag; only schema recreated.");
        }
        break;

    case "status":
        await Status(connectionString);
        break;

    default:
        Console.WriteLine("Usage: dotnet run -- [clear|seed|status] [--org]");
        Console.WriteLine("");
        Console.WriteLine("  clear         Drop + recreate the admin DB (no data).");
        Console.WriteLine("  seed          Same as clear, plus optional data flags.");
        Console.WriteLine("  seed --org    Seed minimal org data (users / depts / group / roles / sample delegation).");
        Console.WriteLine("  status        Report current DB state (table counts).");
        Console.WriteLine("");
        Console.WriteLine("  Dev guard: refuses unless ASPNETCORE_ENVIRONMENT=Development or FLOWCOOK_ALLOW_SEED=1.");
        Console.WriteLine($"  Demo password for seeded users: {Seeder.DemoPassword}");
        return 0;
}

return 0;

static async Task Status(string connectionString)
{
    var options = AdminOptions(connectionString);
    await using var ctx = new AdminDbContext(options);
    Console.WriteLine($"Connection: {connectionString}");
    Console.WriteLine($"Principals: {await ctx.Principals.CountAsync()}");
    Console.WriteLine($"Roles: {await ctx.Roles.CountAsync()}");
    Console.WriteLine($"PrincipalRoles: {await ctx.PrincipalRoles.CountAsync()}");
    Console.WriteLine($"UserDepts: {await ctx.UserDepts.CountAsync()}");
    Console.WriteLine($"DeptParents: {await ctx.DeptParents.CountAsync()}");
    Console.WriteLine($"GroupMembers: {await ctx.GroupMembers.CountAsync()}");
    Console.WriteLine($"Delegations: {await ctx.Delegations.CountAsync()}");
    Console.WriteLine($"UserCredentials: {await ctx.UserCredentials.CountAsync()}");
    Console.WriteLine($"AuditEvents: {await ctx.AuditEvents.CountAsync()}");
}

static DbContextOptions<AdminDbContext> AdminOptions(string connectionString)
{
    // Provider (sqlite | postgres) from config/env; history table matches the
    // running API so SeedCli sees the same migration rows.
    var builder = new DbContextOptionsBuilder<AdminDbContext>();
    Bpm.Admin.Persistence.DbProviderSetup.Configure(
        builder, Bpm.Admin.Persistence.DbProviderSetup.ResolveProvider(null), connectionString);
    return builder.Options;
}
