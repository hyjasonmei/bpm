using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Bpm.Application.Notifications;

/// <summary>
/// Real outbound email transport (MailKit/SMTP). One of the composite sinks —
/// no-ops unless <see cref="SmtpOptions.Enabled"/> is set, so dev / test /
/// sandbox keep using the file + in-app + sandbox-capture sinks only. Sends
/// only when the message lists the <c>email</c> channel and has at least one
/// recipient with an email address.
///
/// Provider-agnostic: works against Azure Communication Services SMTP,
/// SendGrid SMTP, or any relay — set host/port/credentials/security via
/// <c>Bpm:Notifications:Smtp</c> (env in real environments).
/// </summary>
public sealed class SmtpNotifyDispatcher(
    IOptions<SmtpOptions> options,
    ILogger<SmtpNotifyDispatcher> log) : INotifyDispatcher
{
    private readonly SmtpOptions _opt = options.Value;

    public async Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
    {
        if (!_opt.Enabled) return;
        if (!message.Channels.Contains("email")) return;

        var to = message.Recipients
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => new MailboxAddress(r.DisplayName ?? r.Email, r.Email))
            .ToList();
        if (to.Count == 0) return;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_opt.FromName, _opt.FromAddress));
        mime.To.AddRange(to);
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_opt.Host, _opt.Port, SecurityFor(_opt.Security), ct);
            if (!string.IsNullOrEmpty(_opt.Username))
                await client.AuthenticateAsync(_opt.Username, _opt.Password ?? string.Empty, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);

            log.LogInformation(
                "Notify → smtp {Host}:{Port}: {SourceId} | to={To} | subject={Subject}",
                _opt.Host, _opt.Port, message.SourceId, string.Join(",", to.Select(t => t.Address)), message.Subject);
        }
        catch (Exception ex)
        {
            // Best-effort: a mail outage must NOT abort the state-machine
            // transition (the case is already saved) — the in-app bell + audit
            // sinks still record it. Log and swallow.
            log.LogError(ex,
                "Notify → smtp FAILED {Host}:{Port}: {SourceId} | subject={Subject}",
                _opt.Host, _opt.Port, message.SourceId, message.Subject);
        }
    }

    private static SecureSocketOptions SecurityFor(string? security) => security?.ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "ssl" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
        "auto" => SecureSocketOptions.Auto,
        _ => SecureSocketOptions.None,
    };
}
