using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bpm.Application.Notifications;

/// <summary>
/// Writes every dispatched notification to a local plain-text log
/// file. Intended for the POC / sandbox / E2E test path — production
/// deployments swap this out for a real SMTP / Teams / in-app sender.
///
/// <para>
/// TODO(prod): replace with a real email transport (System.Net.Mail,
/// SendGrid, AWS SES, etc.). The interface is stable; only this
/// implementation changes. Keep the file path under
/// <c>Bpm:Notifications:FilePath</c> as a fallback / dev-loop output
/// even after a real sender lands so chef tests don't fight the
/// network.
/// </para>
/// </summary>
public sealed class FileNotifyDispatcher : INotifyDispatcher
{
    private readonly string _path;
    private readonly ILogger<FileNotifyDispatcher> _log;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public FileNotifyDispatcher(IOptions<NotifyDispatcherOptions> options, ILogger<FileNotifyDispatcher> log)
    {
        _path = options.Value.FilePath;
        _log = log;
    }

    public async Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"at: {DateTime.UtcNow:o}");
        sb.AppendLine($"source: {message.SourceId}");
        sb.AppendLine($"channels: {string.Join(",", message.Channels)}");
        sb.AppendLine($"recipients: {FormatRecipients(message.Recipients)}");
        if (message.Context is { Count: > 0 })
        {
            sb.AppendLine("context:");
            foreach (var (k, v) in message.Context)
            {
                sb.AppendLine($"  {k}: {v ?? "(null)"}");
            }
        }
        sb.AppendLine($"subject: {message.Subject}");
        sb.AppendLine("body: |");
        foreach (var line in message.Body.Split('\n'))
        {
            sb.AppendLine($"  {line.TrimEnd('\r')}");
        }
        sb.AppendLine();

        await _gate.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.AppendAllTextAsync(_path, sb.ToString(), Encoding.UTF8, ct);
        }
        finally
        {
            _gate.Release();
        }

        _log.LogInformation(
            "Notify → file {Path}: {SourceId} | {Channels} | subject={Subject}",
            _path, message.SourceId, string.Join(",", message.Channels), message.Subject);
    }

    private static string FormatRecipients(IReadOnlyList<NotifyRecipient> recipients)
    {
        if (recipients.Count == 0) return "(none)";
        return string.Join("; ", recipients.Select(r =>
            r.Email is not null
                ? $"{r.DisplayName ?? r.Email} <{r.Email}>"
                : r.UserId?.ToString() ?? "(unknown)"));
    }
}

public sealed class NotifyDispatcherOptions
{
    /// <summary>
    /// Where <see cref="FileNotifyDispatcher"/> appends rendered
    /// notifications. Relative paths resolve against the API host's
    /// content root; create the directory on first write.
    /// </summary>
    public string FilePath { get; set; } = "var/notifications.txt";
}
