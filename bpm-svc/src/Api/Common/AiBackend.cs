using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Bpm.Api.Common;

/// <summary>
/// AI backend abstraction. Two implementations:
///
/// - <see cref="AnthropicApiBackend"/> — pay-per-token, uses ANTHROPIC_API_KEY.
///   Required for production / multi-user / cloud deployments.
/// - <see cref="ClaudeCliBackend"/> — shells out to the local `claude` CLI,
///   borrowing the developer's Claude Code subscription quota. Single-user
///   only (auth tokens are bound to the OS user that ran `claude /login`).
///   Selected by BPM_AI_BACKEND=cli. Convenient for local dev / demo.
/// </summary>
public interface IAiBackend
{
    string Name { get; }

    /// <summary>Returns a chat reply. Response shape mirrors Anthropic's
    /// /v1/messages so the front-end can read content[].text uniformly.</summary>
    Task<AiResult> ChatAsync(string systemPrompt, JsonElement messages, CancellationToken ct);

    /// <summary>Extracts a structured flow skeleton. Returns the tool input
    /// JSON (schema-validated) — front-end applies it directly to the draft.</summary>
    Task<AiResult> ExtractFlowAsync(
        string systemPrompt,
        string? userText,
        string? imageDataUrl,
        object jsonSchema,
        CancellationToken ct);
}

public record AiResult(int StatusCode, string Body, string ContentType = "application/json");

public static class AiBackendErrors
{
    public static AiResult ConfigureApiKey() => new(503, JsonSerializer.Serialize(new
    {
        error = "configure_api_key",
        message = "後端環境變數 ANTHROPIC_API_KEY 尚未設定，且目前 BPM_AI_BACKEND=api。請執行：export ANTHROPIC_API_KEY=sk-ant-... && dotnet run，或改用 export BPM_AI_BACKEND=cli 走本機 Claude Code。"
    }));

    public static AiResult ClaudeCliMissing(string detail) => new(503, JsonSerializer.Serialize(new
    {
        error = "claude_cli_missing",
        message = $"目前 BPM_AI_BACKEND=cli 但找不到 `claude` CLI（{detail}）。安裝 Claude Code 並 `claude /login` 後重啟，或改用 BPM_AI_BACKEND=api + ANTHROPIC_API_KEY。"
    }));

    public static AiResult Upstream(string msg) => new(502, JsonSerializer.Serialize(new { error = "upstream_failure", message = msg }));

    public static AiResult NotSupported(string what) => new(501, JsonSerializer.Serialize(new
    {
        error = "not_supported_by_backend",
        message = $"目前後端不支援 {what}。CLI backend 暫不支援圖片輸入；請改用 BPM_AI_BACKEND=api 或選 Templates / From Scratch (文字)。"
    }));
}

/// <summary>HTTP path: bpm-svc → Anthropic /v1/messages. Pays per token.</summary>
public sealed class AnthropicApiBackend : IAiBackend
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string? _apiKey;

    public AnthropicApiBackend(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    public string Name => "api";

    public async Task<AiResult> ChatAsync(string systemPrompt, JsonElement messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return AiBackendErrors.ConfigureApiKey();

        var anthropicReq = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 1024,
            system = new object[] { new { type = "text", text = systemPrompt, cache_control = new { type = "ephemeral" } } },
            messages = JsonSerializer.Deserialize<object>(messages.GetRawText())
        };
        return await PostAsync(anthropicReq, ct);
    }

    public async Task<AiResult> ExtractFlowAsync(string systemPrompt, string? userText, string? imageDataUrl, object jsonSchema, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return AiBackendErrors.ConfigureApiKey();

        var content = new List<object>();
        if (!string.IsNullOrWhiteSpace(imageDataUrl))
        {
            var commaIdx = imageDataUrl.IndexOf(',');
            var header = imageDataUrl.Substring(5, commaIdx - 5);
            var mediaType = header.Split(';')[0];
            var base64 = imageDataUrl.Substring(commaIdx + 1);
            content.Add(new { type = "image", source = new { type = "base64", media_type = mediaType, data = base64 } });
            content.Add(new { type = "text", text = "請根據這張流程圖抽出 BPMN skeleton。如果手繪 / 影像不清楚，盡力推斷並在 confidence_notes 中標出不確定的節點。" });
        }
        else if (!string.IsNullOrWhiteSpace(userText))
        {
            content.Add(new { type = "text", text = $"請根據以下流程描述抽出 BPMN skeleton：\n\n{userText}" });
        }
        else
        {
            return new AiResult(400, JsonSerializer.Serialize(new { error = "Either text or imageDataUrl must be provided" }));
        }

        var anthropicReq = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 2048,
            system = systemPrompt,
            tools = new object[] { new { name = "emit_flow_skeleton", description = "Emit the extracted BPMN flow skeleton.", input_schema = jsonSchema } },
            tool_choice = new { type = "tool", name = "emit_flow_skeleton" },
            messages = new object[] { new { role = "user", content } }
        };

        var raw = await PostAsync(anthropicReq, ct);
        if (raw.StatusCode != 200) return raw;

        // Unwrap the tool_use input — front-end wants only the schema payload.
        try
        {
            using var doc = JsonDocument.Parse(raw.Body);
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.GetProperty("type").GetString() == "tool_use" &&
                    block.GetProperty("name").GetString() == "emit_flow_skeleton")
                {
                    return new AiResult(200, block.GetProperty("input").GetRawText());
                }
            }
            return new AiResult(502, JsonSerializer.Serialize(new { error = "no_tool_use", raw = raw.Body }));
        }
        catch (Exception ex)
        {
            return new AiResult(502, JsonSerializer.Serialize(new { error = "parse_failure", message = ex.Message }));
        }
    }

    private async Task<AiResult> PostAsync(object req, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("anthropic");
        using var msg = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = JsonContent.Create(req) };
        msg.Headers.Add("x-api-key", _apiKey!);
        msg.Headers.Add("anthropic-version", "2023-06-01");
        try
        {
            using var res = await http.SendAsync(msg, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return new AiResult((int)res.StatusCode, body);
        }
        catch (Exception ex)
        {
            return AiBackendErrors.Upstream(ex.Message);
        }
    }
}

/// <summary>Spawns `claude -p` per request — uses the dev's Claude Code
/// subscription quota. Single-user, local-only. Image inputs unsupported
/// (the CLI has no documented base64 image flag for non-interactive mode).</summary>
public sealed class ClaudeCliBackend : IAiBackend
{
    private readonly ILogger<ClaudeCliBackend> _logger;
    private readonly string _claudeBin;

    public ClaudeCliBackend(ILogger<ClaudeCliBackend> logger)
    {
        _logger = logger;
        _claudeBin = Environment.GetEnvironmentVariable("BPM_CLAUDE_CLI_PATH") ?? "claude";
    }

    public string Name => "cli";

    public async Task<AiResult> ChatAsync(string systemPrompt, JsonElement messages, CancellationToken ct)
    {
        // Inline the conversation history into a single prompt — multi-turn
        // session resume via --resume would need session-id bookkeeping the
        // wizard doesn't have, and chat replies are short enough that
        // re-sending history each turn is fine.
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("# Conversation so far");
        promptBuilder.AppendLine();
        foreach (var m in messages.EnumerateArray())
        {
            var role = m.GetProperty("role").GetString();
            var contentEl = m.GetProperty("content");
            var contentText = contentEl.ValueKind == JsonValueKind.String
                ? contentEl.GetString()
                : contentEl.GetRawText();
            promptBuilder.AppendLine($"## {role}");
            promptBuilder.AppendLine(contentText);
            promptBuilder.AppendLine();
        }
        promptBuilder.AppendLine("# Now respond as the assistant to the latest user message. Plain text only — no markdown headers.");

        var (exitCode, stdout, stderr) = await RunClaudeAsync(
            systemPrompt: systemPrompt,
            stdinPrompt: promptBuilder.ToString(),
            jsonSchema: null,
            ct: ct);

        if (exitCode == 127) return AiBackendErrors.ClaudeCliMissing(stderr.Length > 0 ? stderr : "command not found");
        if (exitCode != 0)
            return AiBackendErrors.Upstream($"claude CLI exit {exitCode}: {stderr}".Trim());

        // Wrap the plain text in an Anthropic-compatible envelope so the
        // front-end's content[].text parser works without backend awareness.
        var envelope = new
        {
            id = "cli-" + Guid.NewGuid().ToString("N")[..12],
            type = "message",
            role = "assistant",
            model = "claude-code-cli",
            content = new object[] { new { type = "text", text = stdout.Trim() } },
            stop_reason = "end_turn",
            backend = "cli"
        };
        return new AiResult(200, JsonSerializer.Serialize(envelope));
    }

    public async Task<AiResult> ExtractFlowAsync(string systemPrompt, string? userText, string? imageDataUrl, object jsonSchema, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(imageDataUrl))
        {
            return AiBackendErrors.NotSupported("圖片輸入（CLI backend 沒有 base64 image 旗標）");
        }
        if (string.IsNullOrWhiteSpace(userText))
        {
            return new AiResult(400, JsonSerializer.Serialize(new { error = "Missing text" }));
        }

        // --json-schema in this CLI version only *validates* output, it does
        // not force the model into structured mode. We compensate with a
        // hard-line prompt: pure JSON only, no prose / markdown / tables.
        var schemaJson = JsonSerializer.Serialize(jsonSchema);
        var prompt = $@"請根據以下流程描述抽出 BPMN skeleton：

{userText}

⚠️ 嚴格輸出要求 — 你的整個回覆**只能是一個** JSON 物件，符合下方 schema。

絕對不要：
- markdown code fence (```json ... ```)
- 表格、條列、解釋文字
- 任何不在 schema 內的欄位

JSON Schema：
{schemaJson}

現在回覆 JSON 物件：";

        var (exitCode, stdout, stderr) = await RunClaudeAsync(
            systemPrompt: systemPrompt,
            stdinPrompt: prompt,
            jsonSchema: jsonSchema,
            ct: ct);

        if (exitCode == 127) return AiBackendErrors.ClaudeCliMissing(stderr.Length > 0 ? stderr : "command not found");
        if (exitCode != 0) return AiBackendErrors.Upstream($"claude CLI exit {exitCode}: {stderr}".Trim());

        // Output should already be schema-conforming JSON; pass it straight
        // through. Strip any markdown fences just in case the model wrapped it.
        var raw = stdout.Trim();
        if (raw.StartsWith("```"))
        {
            var firstNewline = raw.IndexOf('\n');
            if (firstNewline > 0) raw = raw[(firstNewline + 1)..];
            if (raw.EndsWith("```")) raw = raw[..^3].TrimEnd();
        }
        return new AiResult(200, raw);
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunClaudeAsync(
        string systemPrompt,
        string stdinPrompt,
        object? jsonSchema,
        CancellationToken ct)
    {
        // 60s hard ceiling on subprocess to avoid wedging the request when
        // claude hangs on auth / network / something unexpected.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));
        return await RunClaudeInner(systemPrompt, stdinPrompt, jsonSchema, timeoutCts.Token);
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunClaudeInner(
        string systemPrompt,
        string stdinPrompt,
        object? jsonSchema,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_claudeBin)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-p");
        // NB: deliberately NOT --bare. --bare would strip CLAUDE.md / hooks /
        // auto-memory for cleaner runs, but it also disables OAuth keychain
        // reads ("Anthropic auth is strictly ANTHROPIC_API_KEY or apiKeyHelper")
        // which defeats the whole point of this backend (borrowing the dev's
        // Claude Code subscription). The system prompt is strict enough to
        // dominate any leaked auto-memory.
        psi.ArgumentList.Add("--no-session-persistence");
        psi.ArgumentList.Add("--setting-sources");
        psi.ArgumentList.Add("user"); // skip project / local CLAUDE.md, keep user OAuth
        // Block user-level MCP servers from loading in the subprocess. Without this,
        // a parent Claude Code session that owns a single-connection MCP (e.g. a
        // Telegram bot — Bot API getUpdates only allows one polling client per
        // token) gets kicked off when the child connects. --strict-mcp-config +
        // an empty config means the child loads zero MCP servers regardless of
        // user settings.
        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add("{\"mcpServers\":{}}");
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add("claude-sonnet-4-6");
        psi.ArgumentList.Add("--tools");
        psi.ArgumentList.Add(""); // disable all built-in tools — we just want the model
        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(systemPrompt);
        psi.ArgumentList.Add("--max-budget-usd");
        psi.ArgumentList.Add("0.50");
        // NB: --json-schema is intentionally NOT passed. In CLI 2.1.x it's
        // validation-only (post-hoc), and combining it with very long argv
        // values caused the process to hang in non-tty mode. We embed the
        // schema in the prompt instead and the model follows it.
        // Run from a stable working dir so we don't pick up whichever CLAUDE.md
        // happens to be near the dotnet process. tmp avoids any project memory.
        psi.WorkingDirectory = Path.GetTempPath();

        Process proc;
        try
        {
            proc = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (127, "", ex.Message);
        }

        await proc.StandardInput.WriteAsync(stdinPrompt.AsMemory(), ct);
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* ignore */ }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (proc.ExitCode != 0)
            _logger.LogWarning("claude CLI exited {Code}: {Stderr}", proc.ExitCode, stderr);
        return (proc.ExitCode, stdout, stderr);
    }
}
