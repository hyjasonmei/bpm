using System.Net.Http.Json;

namespace Bpm.ChefAgent;

/// <summary>
/// Fire-and-forget Telegram notifications. No-ops when no telegram config is
/// present, and never throws — a notification failure must not abort a poll.
/// </summary>
public sealed class TelegramNotifier
{
    private readonly TelegramConfig? _cfg;
    private readonly HttpClient _http;

    public TelegramNotifier(TelegramConfig? cfg, HttpClient? http = null)
    {
        _cfg = cfg;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        if (_cfg is null) { Console.WriteLine($"[tg-noop] {text}"); return; }
        try
        {
            await _http.PostAsJsonAsync(
                $"https://api.telegram.org/bot{_cfg.BotToken}/sendMessage",
                new { chat_id = _cfg.ChatId, text }, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tg-fail] {ex.Message}: {text}");
        }
    }
}
