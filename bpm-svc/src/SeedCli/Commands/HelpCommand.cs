namespace Bpm.SeedCli.Commands;

public static class HelpCommand
{
    public const string HelpText = """
        bpm-seed — BPM development & demo bootstrap CLI

        USAGE:
          dotnet run --project bpm-svc/src/SeedCli -- <command> [options]

        COMMANDS:
          reset
              Drop the SQLite file and re-apply EF migrations to a fresh DB.
              No seed data is written. Combine with `seed` to populate.

          seed [--include-bundles]
              Idempotent. Seed the 13-user persona shape (departments,
              users, roles, role assignments). Pass --include-bundles to
              also build & install every sample_specs/*.json as a SpecBundle
              row in Status=Pending. Repro is NOT executed — open Flow
              Library and click "Repro Check" per bundle to verify.

          status
              Print row counts (users / departments / process instances /
              spec bundles) plus current sandbox mode + clock offset.

          help
              Print this help text.

        EXAMPLES:
          # Fresh dev environment with full demo state
          dotnet run --project bpm-svc/src/SeedCli -- reset
          dotnet run --project bpm-svc/src/SeedCli -- seed --include-bundles

          # After branch switch — re-apply seed (no-op if already present)
          dotnet run --project bpm-svc/src/SeedCli -- seed

          # Health snapshot
          dotnet run --project bpm-svc/src/SeedCli -- status

        OPTIONS:
          --sample-specs <dir>   Path to sample_specs/ (default: <repo>/sample_specs).
          --connection <conn>    Override SQLite connection string. By default
                                 reads ConnectionStrings:Default from appsettings.json.
        """;

    public static int Run()
    {
        Console.WriteLine(HelpText);
        return 0;
    }
}
