using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Bpm.Admin.Api.Common;

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
    /// /v1/messages so the front-end can read content[] uniformly. When
    /// <paramref name="tools"/> is non-null, the model may emit tool_use
    /// blocks that the front-end applies to the draft canvas.</summary>
    Task<AiResult> ChatAsync(string systemPrompt, JsonElement messages, JsonElement? tools, CancellationToken ct);

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
    private readonly ILogger<AnthropicApiBackend> _logger;
    private readonly string? _apiKey;

    public AnthropicApiBackend(IHttpClientFactory httpFactory, ILogger<AnthropicApiBackend> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    public string Name => "api";

    public async Task<AiResult> ChatAsync(string systemPrompt, JsonElement messages, JsonElement? tools, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return AiBackendErrors.ConfigureApiKey();

        const string model = "claude-sonnet-4-6";
        var hasTools = tools is { ValueKind: JsonValueKind.Array } t && t.GetArrayLength() > 0;
        var fields = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 1024,
            ["system"] = new object[] { new { type = "text", text = systemPrompt, cache_control = new { type = "ephemeral" } } },
            ["messages"] = JsonSerializer.Deserialize<object>(messages.GetRawText()),
        };
        if (hasTools)
        {
            fields["tools"] = JsonSerializer.Deserialize<object>(tools!.Value.GetRawText());
        }
        var sw = Stopwatch.StartNew();
        var result = await PostAsync(fields, ct);
        sw.Stop();
        _logger.LogInformation(
            "AI api/chat: model={Model} status={Status} duration_ms={Duration} body_chars={Bytes} tools={HasTools}",
            model, result.StatusCode, sw.ElapsedMilliseconds, result.Body.Length, hasTools);
        return result;
    }

    public async Task<AiResult> ExtractFlowAsync(string systemPrompt, string? userText, string? imageDataUrl, object jsonSchema, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return AiBackendErrors.ConfigureApiKey();

        var inputKind = !string.IsNullOrWhiteSpace(imageDataUrl) ? "image" : "description";
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

        // Opus has the strongest vision in 4.x — worth the extra latency for
        // diagram OCR. Text-only extracts stay on Sonnet (cheaper, fast enough,
        // accuracy parity for prose). Mirrors the CLI backend's split.
        var model = !string.IsNullOrWhiteSpace(imageDataUrl) ? "claude-opus-4-7" : "claude-sonnet-4-6";
        var anthropicReq = new
        {
            model,
            max_tokens = 2048,
            system = systemPrompt,
            tools = new object[] { new { name = "emit_flow_skeleton", description = "Emit the extracted BPMN flow skeleton.", input_schema = jsonSchema } },
            tool_choice = new { type = "tool", name = "emit_flow_skeleton" },
            messages = new object[] { new { role = "user", content } }
        };

        var sw = Stopwatch.StartNew();
        var raw = await PostAsync(anthropicReq, ct);
        sw.Stop();
        _logger.LogInformation(
            "AI api/spec-extract: model={Model} input={InputKind} status={Status} duration_ms={Duration} body_chars={Bytes}",
            model, inputKind, raw.StatusCode, sw.ElapsedMilliseconds, raw.Body.Length);
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
        var configured = Environment.GetEnvironmentVariable("BPM_CLAUDE_CLI_PATH") ?? "claude";
        _claudeBin = ResolveBin(configured);
    }

    // Process.Start with UseShellExecute=false skips PATHEXT on Windows, so a
    // bare "claude" misses claude.cmd / claude.exe even when the shell resolves
    // it fine. POSIX execvp handles PATH lookup natively — no-op on mac/linux.
    private static string ResolveBin(string name)
    {
        if (!OperatingSystem.IsWindows()) return name;
        if (Path.IsPathRooted(name)
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar)
            || Path.HasExtension(name))
            return name;

        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return name;
    }

    public string Name => "cli";

    // Sentinels for the CLI tool-use protocol. The model wraps any tool call
    // between these markers; everything outside is plain text. Long ASCII
    // sentinels prevent collision with anything the model might naturally
    // emit.
    private const string ToolOpen = "[[TOOL_CALL]]";
    private const string ToolClose = "[[/TOOL_CALL]]";

    public async Task<AiResult> ChatAsync(string systemPrompt, JsonElement messages, JsonElement? tools, CancellationToken ct)
    {
        // CLI's -p mode has no structured tools parameter. We emulate it: tool
        // schemas go into the prompt with a delimited-output contract, and the
        // backend parses the model's reply back into Anthropic-shaped tool_use
        // blocks. Reliability is lower than real tool use (text contract vs
        // structured), but for chat-driven small mutations it's fine — and the
        // wizard otherwise wouldn't have AI-applies-changes on the CLI path,
        // which is the BPM_AI_BACKEND=cli default.
        var toolsRequested = tools is { ValueKind: JsonValueKind.Array } te && te.GetArrayLength() > 0;

        var promptBuilder = new StringBuilder();

        if (toolsRequested)
        {
            promptBuilder.AppendLine("# Available tools");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("You have the following tool(s). Each tool's input must conform to its JSON Schema.");
            promptBuilder.AppendLine();
            foreach (var tool in tools!.Value.EnumerateArray())
            {
                var tname = tool.GetProperty("name").GetString();
                var tdesc = tool.TryGetProperty("description", out var dd) ? dd.GetString() : "";
                var tschema = tool.GetProperty("input_schema").GetRawText();
                promptBuilder.AppendLine($"## {tname}");
                promptBuilder.AppendLine(tdesc);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("Input schema:");
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine(tschema);
                promptBuilder.AppendLine("```");
                promptBuilder.AppendLine();
            }
            promptBuilder.AppendLine("## How to call a tool");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("If the user asks for a concrete change to the draft, end your reply with EXACTLY this block (and nothing after it):");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine(ToolOpen);
            promptBuilder.AppendLine("{\"name\":\"<tool_name>\",\"input\":{...the complete tool input matching the schema...}}");
            promptBuilder.AppendLine(ToolClose);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Rules:");
            promptBuilder.AppendLine($"- Sentinels must be on their own lines, exact text: `{ToolOpen}` and `{ToolClose}`");
            promptBuilder.AppendLine("- Inside the sentinels: pure JSON. No markdown fences, no comments.");
            promptBuilder.AppendLine("- Tool input is the COMPLETE state for the slice, not a patch (include existing items + new/modified ones).");
            promptBuilder.AppendLine("- Do not call the tool for purely informational questions — text only is fine.");
            promptBuilder.AppendLine();
        }

        // Inline the conversation history into a single prompt — multi-turn
        // session resume via --resume would need session-id bookkeeping the
        // wizard doesn't have, and chat replies are short enough that
        // re-sending history each turn is fine.
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
        promptBuilder.AppendLine("# Now respond as the assistant to the latest user message.");
        if (toolsRequested)
        {
            promptBuilder.AppendLine($"If a draft change is warranted, append the {ToolOpen}…{ToolClose} block at the very end.");
        }

        var sw = Stopwatch.StartNew();
        var (exitCode, stdout, stderr) = await RunClaudeAsync(
            systemPrompt: systemPrompt,
            stdinPrompt: promptBuilder.ToString(),
            jsonSchema: null,
            ct: ct);
        sw.Stop();
        _logger.LogInformation(
            "AI cli/chat: model=claude-sonnet-4-6 exit={Exit} duration_ms={Duration} stdout_chars={StdOut} stderr_chars={StdErr} tools={HasTools}",
            exitCode, sw.ElapsedMilliseconds, stdout.Length, stderr.Length, toolsRequested);

        if (exitCode == 127) return AiBackendErrors.ClaudeCliMissing(stderr.Length > 0 ? stderr : "command not found");
        if (exitCode != 0)
            return AiBackendErrors.Upstream($"claude CLI exit {exitCode}: {stderr}".Trim());

        var (textPart, toolBlock) = ParseToolEmulationOutput(stdout);

        // Build content[] with text first, optionally tool_use second — same
        // shape as Anthropic's API response so the front-end stays uniform.
        var content = new List<object>();
        if (!string.IsNullOrWhiteSpace(textPart))
            content.Add(new { type = "text", text = textPart });
        if (toolBlock != null)
            content.Add(toolBlock);
        if (content.Count == 0)
            content.Add(new { type = "text", text = "(empty CLI response)" });

        var envelope = new
        {
            id = "cli-" + Guid.NewGuid().ToString("N")[..12],
            type = "message",
            role = "assistant",
            model = "claude-code-cli",
            content = content,
            stop_reason = toolBlock != null ? "tool_use" : "end_turn",
            backend = "cli",
        };
        return new AiResult(200, JsonSerializer.Serialize(envelope));
    }

    private static (string text, object? toolUse) ParseToolEmulationOutput(string raw)
    {
        var trimmed = raw.Trim();
        var openIdx = trimmed.IndexOf(ToolOpen, StringComparison.Ordinal);
        if (openIdx < 0) return (trimmed, null);

        var closeIdx = trimmed.IndexOf(ToolClose, openIdx + ToolOpen.Length, StringComparison.Ordinal);
        if (closeIdx < 0)
        {
            // Open without close — treat the whole thing as text so the user
            // can still see what the model said.
            return (trimmed, null);
        }

        var text = trimmed.Substring(0, openIdx).TrimEnd();
        var jsonChunk = trimmed.Substring(openIdx + ToolOpen.Length, closeIdx - (openIdx + ToolOpen.Length)).Trim();

        // Strip markdown fences if the model wrapped the JSON anyway.
        if (jsonChunk.StartsWith("```"))
        {
            var nl = jsonChunk.IndexOf('\n');
            if (nl > 0) jsonChunk = jsonChunk[(nl + 1)..];
            if (jsonChunk.EndsWith("```")) jsonChunk = jsonChunk[..^3].TrimEnd();
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonChunk);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (trimmed, null);
            if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) return (trimmed, null);
            if (!root.TryGetProperty("input", out var inputEl)) return (trimmed, null);

            var toolUse = new
            {
                type = "tool_use",
                id = "cli-tool-" + Guid.NewGuid().ToString("N")[..8],
                name = nameEl.GetString()!,
                input = JsonSerializer.Deserialize<object>(inputEl.GetRawText()),
            };
            return (text, toolUse);
        }
        catch (JsonException)
        {
            // Malformed JSON in tool block — surface the whole reply as text
            // so the user can see what went wrong.
            return (trimmed, null);
        }
    }

    public async Task<AiResult> ExtractFlowAsync(string systemPrompt, string? userText, string? imageDataUrl, object jsonSchema, CancellationToken ct)
    {
        var schemaJson = JsonSerializer.Serialize(jsonSchema);
        string prompt;
        string? tempImagePath = null;
        string? extraDir = null;
        string toolsList = "";          // "" = disable all built-in tools
        string model = "claude-sonnet-4-6";

        try
        {
            if (!string.IsNullOrWhiteSpace(imageDataUrl))
            {
                // Spill the data URL to disk so the CLI can Read it. -p mode has
                // no base64 image flag, so this is the only way to feed an image
                // through claude in non-interactive mode. Opus has the strongest
                // vision in 4.x — worth the extra latency for diagram OCR.
                tempImagePath = WriteImageToTemp(imageDataUrl);
                extraDir = Path.GetDirectoryName(tempImagePath);
                toolsList = "Read";
                model = "claude-opus-4-7";

                prompt = $@"請 Read 這張流程圖：{tempImagePath}

讀完後，根據圖中的節點與連線抽出 BPMN skeleton。手繪 / 影像不清楚的部分，盡力推斷並在 confidence_notes 中標註。

⚠️ 嚴格輸出要求 — 你回覆的最後**只能是一個** JSON 物件，符合下方 schema。
- 可以先用文字思考（不會被 parse），但最終結果只留 JSON
- 絕對不要 markdown code fence (```json ...```)
- 不要任何不在 schema 內的欄位

JSON Schema：
{schemaJson}

讀完圖後，回覆完整 JSON 物件：";
            }
            else if (!string.IsNullOrWhiteSpace(userText))
            {
                // --json-schema in this CLI version only *validates* output, it
                // does not force the model into structured mode. We compensate
                // with a hard-line prompt: pure JSON only, no prose / fences.
                prompt = $@"請根據以下流程描述抽出 BPMN skeleton：

{userText}

⚠️ 嚴格輸出要求 — 你的整個回覆**只能是一個** JSON 物件，符合下方 schema。

絕對不要：
- markdown code fence (```json ... ```)
- 表格、條列、解釋文字
- 任何不在 schema 內的欄位

JSON Schema：
{schemaJson}

現在回覆 JSON 物件：";
            }
            else
            {
                return new AiResult(400, JsonSerializer.Serialize(new { error = "Missing text or imageDataUrl" }));
            }

            var inputKind = !string.IsNullOrWhiteSpace(imageDataUrl) ? "image" : "description";
            var sw = Stopwatch.StartNew();
            var (exitCode, stdout, stderr) = await RunClaudeAsync(
                systemPrompt: systemPrompt,
                stdinPrompt: prompt,
                jsonSchema: jsonSchema,
                allowedTools: toolsList,
                addDir: extraDir,
                model: model,
                ct: ct);
            sw.Stop();
            _logger.LogInformation(
                "AI cli/spec-extract: model={Model} input={InputKind} exit={Exit} duration_ms={Duration} stdout_chars={StdOut} stderr_chars={StdErr}",
                model, inputKind, exitCode, sw.ElapsedMilliseconds, stdout.Length, stderr.Length);

            if (exitCode == 127) return AiBackendErrors.ClaudeCliMissing(stderr.Length > 0 ? stderr : "command not found");
            if (exitCode != 0) return AiBackendErrors.Upstream($"claude CLI exit {exitCode}: {stderr}".Trim());

            // Output should be schema-conforming JSON. For image runs the model
            // may include reasoning text before the JSON — extract the last
            // JSON object. Also strip any markdown fences as a belt-and-braces.
            var raw = ExtractJsonObject(stdout.Trim());
            return new AiResult(200, raw);
        }
        finally
        {
            if (tempImagePath != null)
            {
                try { File.Delete(tempImagePath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    private static string WriteImageToTemp(string dataUrl)
    {
        // dataUrl shape: "data:image/png;base64,iVBOR..."
        var commaIdx = dataUrl.IndexOf(',');
        if (commaIdx <= 5) throw new ArgumentException("Malformed data URL");
        var header = dataUrl.Substring(5, commaIdx - 5);
        var mediaType = header.Split(';')[0];
        var ext = mediaType.Split('/').LastOrDefault() ?? "png";
        var base64 = dataUrl[(commaIdx + 1)..];

        var dir = Environment.GetEnvironmentVariable("BPM_UPLOAD_DIR")
                  ?? Path.Combine(Path.GetTempPath(), "bpm-uploads");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"flow-{Guid.NewGuid():N}.{ext}");
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    private static string ExtractJsonObject(string raw)
    {
        // Strip markdown fences if present.
        if (raw.StartsWith("```"))
        {
            var firstNewline = raw.IndexOf('\n');
            if (firstNewline > 0) raw = raw[(firstNewline + 1)..];
            if (raw.EndsWith("```")) raw = raw[..^3].TrimEnd();
        }

        // If reasoning text precedes the JSON, walk back from the last '}' to
        // its matching '{' (depth-tracking, with naive string handling).
        var lastBrace = raw.LastIndexOf('}');
        if (lastBrace < 0) return raw;
        int depth = 0;
        bool inString = false;
        for (int i = lastBrace; i >= 0; i--)
        {
            var c = raw[i];
            if (c == '"' && (i == 0 || raw[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '}') depth++;
            else if (c == '{')
            {
                depth--;
                if (depth == 0) return raw.Substring(i, lastBrace - i + 1);
            }
        }
        return raw;
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunClaudeAsync(
        string systemPrompt,
        string stdinPrompt,
        object? jsonSchema,
        string allowedTools = "",
        string? addDir = null,
        string model = "claude-sonnet-4-6",
        CancellationToken ct = default)
    {
        // 60s hard ceiling on subprocess to avoid wedging the request when
        // claude hangs on auth / network / something unexpected. Image runs
        // need a longer budget — vision adds latency.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = string.IsNullOrEmpty(allowedTools) ? 90 : 180;
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));
        return await RunClaudeInner(systemPrompt, stdinPrompt, jsonSchema, allowedTools, addDir, model, timeoutCts.Token);
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunClaudeInner(
        string systemPrompt,
        string stdinPrompt,
        object? jsonSchema,
        string allowedTools,
        string? addDir,
        string model,
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
        psi.ArgumentList.Add(model);
        psi.ArgumentList.Add("--tools");
        psi.ArgumentList.Add(allowedTools); // "" disables all built-in tools; "Read" needed for image flow
        if (!string.IsNullOrEmpty(addDir))
        {
            // Allow Read access to the upload dir without granting the whole
            // user $HOME. CLI default cwd (Path.GetTempPath()) doesn't include
            // bpm-uploads/, so without this --add-dir Read fails on path checks.
            psi.ArgumentList.Add("--add-dir");
            psi.ArgumentList.Add(addDir);
            // Non-interactive (-p) — any permission prompt would deadlock the
            // request. We only allow Read of our own freshly-written file in a
            // dedicated dir, so bypassing prompts is contained.
            psi.ArgumentList.Add("--permission-mode");
            psi.ArgumentList.Add("bypassPermissions");
        }
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

        System.Diagnostics.Process proc;
        try
        {
            proc = System.Diagnostics.Process.Start(psi)!;
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
