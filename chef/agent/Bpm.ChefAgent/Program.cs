using Bpm.ChefAgent;

// Entry point is fleshed out into the full poll loop in C4. For now it wires
// config load + single-instance lock so the skeleton is runnable end-to-end.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Bpm.ChefAgent <config.json>");
    return 2;
}

AgentConfig config;
try
{
    config = AgentConfig.Load(args[0]);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"config load failed: {ex.Message}");
    return 2;
}

using var heldLock = SingleInstanceLock.TryAcquire(config.LockFilePath);
if (heldLock is null)
{
    Console.WriteLine("another chef-agent instance holds the lock — exiting.");
    return 0;
}

Console.WriteLine($"chef-agent: {config.EnabledEnvironments.Count()} enabled environment(s). Poll loop lands in C4.");
return 0;
