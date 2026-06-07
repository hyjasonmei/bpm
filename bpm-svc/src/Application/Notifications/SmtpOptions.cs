namespace Bpm.Application.Notifications;

/// <summary>
/// SMTP transport config for <see cref="SmtpNotifyDispatcher"/>. Bound from
/// <c>Bpm:Notifications:Smtp</c>. Disabled by default so dev / test / sandbox
/// never try to send real mail — the deployment turns it on and points it at a
/// real relay (Azure Communication Services SMTP, SendGrid SMTP, etc.) via env.
/// </summary>
public sealed class SmtpOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@flowcook.ai";
    public string FromName { get; set; } = "flowcook";

    /// <summary>none | starttls | ssl | auto — maps to MailKit SecureSocketOptions.</summary>
    public string Security { get; set; } = "none";
}
