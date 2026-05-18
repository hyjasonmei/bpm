using Bpm.Admin.Persistence;
using Bpm.Admin.SeedCli;
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

var connectionString = config.GetConnectionString("Admin")
    ?? "Data Source=admin.dev.db";

var sub = args.Length > 0 ? args[0] : "help";
var includeOrg = args.Any(a => a == "--org");

switch (sub)
{
    case "clear":
        await Clear(connectionString);
        Console.WriteLine("Admin DB dropped + recreated.");
        Console.WriteLine("(bpm DB drop will be wired once flowcook-step4 lands.)");
        break;

    case "seed":
        await Clear(connectionString);
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

static async Task Clear(string connectionString)
{
    var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connectionString).Options;
    await using var ctx = new AdminDbContext(options);
    await ctx.Database.EnsureDeletedAsync();
    await ctx.Database.MigrateAsync();
}

static async Task Status(string connectionString)
{
    var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connectionString).Options;
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
