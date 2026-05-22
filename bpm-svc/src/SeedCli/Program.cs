using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Services;
using Bpm.Application.Spec.Bundle;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.SeedCli.Commands;
using Bpm.SeedCli.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bpm.SeedCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            return HelpCommand.Run();
        }

        var command = args[0].ToLowerInvariant();
        var includeBundles = args.Contains("--include-bundles");
        var sampleSpecsDir = ResolveSampleSpecsDir(args);
        var connectionOverride = ResolveOption(args, "--connection");

        // Plain ConfigurationBuilder (no Hosting) — the CLI is a single-shot
        // program; the host's lifetime/hosted-services machinery is overkill
        // and was observed to deadlock when the binary runs detached from a
        // TTY (stdout redirected to a regular file).
        var apiAppsettings = ResolveApiAppsettings();
        var configBuilder = new ConfigurationBuilder();
        if (apiAppsettings is not null)
            configBuilder.AddJsonFile(apiAppsettings, optional: true, reloadOnChange: false);
        configBuilder.AddEnvironmentVariables(prefix: "BPM_");
        var configuration = configBuilder.Build();

        if (!string.IsNullOrWhiteSpace(connectionOverride))
        {
            configuration["ConnectionStrings:Default"] = connectionOverride;
        }

        var connStr = configuration.GetConnectionString("Default") ?? "Data Source=bpm.db";
        connStr = Bpm.Persistence.DbPathResolver.Normalize(connStr);

        // Hand-roll just the DI we need (AppDbContext + audit interceptor +
        // bundle build/parse pipeline). This intentionally avoids
        // AddPersistence + AddApplication: the full graph drags in MediatR /
        // FluentValidation reflection scans + 25 application services that
        // SeedCli never touches.
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.IncludeScopes = false;
                o.TimestampFormat = "HH:mm:ss ";
            });
            b.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<SystemClock>();
        // Two clocks: SystemClock for the audit interceptor (real wall-time
        // for CreatedAt / UpdatedAt), and a fixed FrozenClock for the bundle
        // builder so the manifest's exportedAt is reproducible — without it
        // every SeedCli run would produce a new ManifestChecksum and the
        // ManifestChecksum-based idempotency guard would never trigger.
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<SystemClock>());
        services.AddSingleton<ICurrentUser>(new SeedCurrentUser());
        services.AddSingleton<AuditSaveChangesInterceptor>(sp => new AuditSaveChangesInterceptor(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ICurrentUser>()));
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlite(connStr);
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // Bundle build/parse pipeline only — no validator (we don't enforce
        // cross-file consistency for SeedCli; the bundle is built in-process
        // from spec files we already trust).
        services.AddSingleton<SpecMdRenderer>();
        services.AddSingleton<WalkthroughRenderer>();
        services.AddSingleton<ChangelogRenderer>();
        services.AddSingleton<BundleBuildValidator>();
        // BundleBuilder wired with a FROZEN clock — the manifest's exportedAt
        // is hashed into ManifestChecksum, so without a stable clock the
        // ManifestChecksum-based idempotency guard would never fire on rerun.
        services.AddSingleton<IBundleBuilder>(sp => new BundleBuilder(
            sp.GetRequiredService<BundleBuildValidator>(),
            sp.GetRequiredService<SpecMdRenderer>(),
            sp.GetRequiredService<WalkthroughRenderer>(),
            sp.GetRequiredService<ChangelogRenderer>(),
            new FrozenClock()));
        services.AddSingleton<IBundleParser, BundleParser>();
        services.AddScoped<BundleInstaller>();

        await using var provider = services.BuildServiceProvider(validateScopes: false);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("bpm-seed");

        var sqliteFilePath = TryExtractSqliteFile(connStr);
        logger.LogInformation("[INFO] connection: {Conn}", connStr);
        if (sqliteFilePath is not null)
            logger.LogInformation("[INFO] sqlite file: {Path}", sqliteFilePath);

        return command switch
        {
            "reset"   => await ResetCommand.RunAsync(sqliteFilePath, db, logger),
            "seed"    => await SeedCommand.RunAsync(includeBundles, sampleSpecsDir, sp, db, logger),
            "status"  => await StatusCommand.RunAsync(sqliteFilePath, db, logger),
            "help"    => HelpCommand.Run(),
            _ => UnknownCommand(command),
        };
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: '{command}'.");
        Console.Error.WriteLine();
        Console.WriteLine(HelpCommand.HelpText);
        return 2;
    }

    private static string? ResolveOption(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static string ResolveSampleSpecsDir(string[] args)
    {
        var explicitArg = ResolveOption(args, "--sample-specs");
        if (!string.IsNullOrWhiteSpace(explicitArg))
            return Path.GetFullPath(explicitArg);

        var probe = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(probe, "sample_specs");
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent) || parent == probe) break;
            probe = parent;
        }

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "sample_specs");
        return Path.GetFullPath(cwd);
    }

    private static string? ResolveApiAppsettings()
    {
        var probe = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(probe, "bpm-svc", "src", "Api", "appsettings.json");
            if (File.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent) || parent == probe) break;
            probe = parent;
        }
        return null;
    }

    /// <summary>
    /// Parse the SQLite "Data Source=path" key out of a connection string.
    /// Returns null when not a SQLite connection string (or when the path
    /// can't be resolved).
    /// </summary>
    public static string? TryExtractSqliteFile(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (string.Equals(key, "Data Source", StringComparison.OrdinalIgnoreCase)
             || string.Equals(key, "DataSource", StringComparison.OrdinalIgnoreCase)
             || string.Equals(key, "Filename", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Equals(":memory:", StringComparison.OrdinalIgnoreCase)) return null;
                return Path.IsPathRooted(value) ? value : Path.GetFullPath(value);
            }
        }
        return null;
    }

    /// <summary>
    /// Audit interceptor needs an ICurrentUser. SeedCli runs as the
    /// server-side bootstrap actor — every write is attributed to "bpm-seed".
    /// </summary>
    private sealed class SeedCurrentUser : ICurrentUser
    {
        public string? Id => "bpm-seed";
        public bool IsAuthenticated => true;
        public Guid? ImpersonatedById => null;
        public Guid? ImpersonationSessionId => null;
    }

    /// <summary>
    /// Stable wall-clock used by the BundleBuilder so the manifest's
    /// <c>exportedAt</c> field — and therefore the manifest checksum — is
    /// reproducible across SeedCli invocations. Pinned to the project's
    /// canonical fixture date.
    /// </summary>
    private sealed class FrozenClock : IClock
    {
        private static readonly DateTime Pinned = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Pinned;
        public DateOnly TodayInTaipei() => DateOnly.FromDateTime(Pinned.AddHours(8));
    }
}
