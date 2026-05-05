using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bpm.Domain.Spec;

/// Discriminator-aware (de)serialiser for ActorRef. Reads the `type` field
/// first, then dispatches to the matching record constructor. Round-trip
/// safe — emitted JSON always carries `type` as the first key after the
/// curly brace.
public sealed class ActorRefJsonConverter : JsonConverter<ActorRef>
{
    public override ActorRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("ActorRef must be an object");

        using var doc = JsonDocument.ParseValue(ref reader);
        return FromElement(doc.RootElement, options);
    }

    internal static ActorRef FromElement(JsonElement el, JsonSerializerOptions options)
    {
        if (!el.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            throw new JsonException("ActorRef requires a string `type` field");

        var type = typeEl.GetString();
        ActorRef result = type switch
        {
            "expr" => new ExprActorRef(GetRequiredString(el, "path")),
            "role" => new RoleActorRef(GetRequiredString(el, "code")),
            "group" => new GroupActorRef(GetRequiredString(el, "id")),
            "user" => new UserActorRef(GetRequiredString(el, "id")),
            "conditional" => ReadConditional(el, options),
            "collection" => ReadCollection(el, options),
            _ => throw new JsonException($"Unknown ActorRef type '{type}'. Allowed: expr, role, group, user, conditional, collection."),
        };

        if (el.TryGetProperty("fallback", out var fb) && fb.ValueKind != JsonValueKind.Null)
        {
            var fallback = FromElement(fb, options);
            result = result with { Fallback = fallback };
        }
        return result;
    }

    private static ConditionalActorRef ReadConditional(JsonElement el, JsonSerializerOptions options)
    {
        if (!el.TryGetProperty("condition", out var condEl) || condEl.ValueKind != JsonValueKind.Object)
            throw new JsonException("conditional ActorRef requires `condition` object");
        var field = GetRequiredString(condEl, "field");
        var op = GetRequiredString(condEl, "op");
        if (!condEl.TryGetProperty("value", out var valEl))
            throw new JsonException("condition requires `value`");
        var condition = new ActorRefCondition(field, op, valEl.Clone());

        if (!el.TryGetProperty("then", out var thenEl)) throw new JsonException("conditional requires `then`");
        if (!el.TryGetProperty("else", out var elseEl)) throw new JsonException("conditional requires `else`");

        return new ConditionalActorRef(condition, FromElement(thenEl, options), FromElement(elseEl, options));
    }

    private static CollectionActorRef ReadCollection(JsonElement el, JsonSerializerOptions options)
    {
        var mode = GetRequiredString(el, "mode");
        if (mode is not "any" and not "all")
            throw new JsonException($"collection.mode must be 'any' or 'all', got '{mode}'");
        if (!el.TryGetProperty("actors", out var actorsEl) || actorsEl.ValueKind != JsonValueKind.Array)
            throw new JsonException("collection requires `actors` array");

        var actors = new List<ActorRef>();
        foreach (var item in actorsEl.EnumerateArray())
            actors.Add(FromElement(item, options));

        int? min = null;
        if (el.TryGetProperty("min_approvals", out var minEl) && minEl.ValueKind == JsonValueKind.Number)
            min = minEl.GetInt32();

        return new CollectionActorRef(mode, actors, min);
    }

    private static string GetRequiredString(JsonElement el, string field)
    {
        if (!el.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String)
            throw new JsonException($"ActorRef.{field} must be a string");
        var s = v.GetString();
        if (string.IsNullOrEmpty(s))
            throw new JsonException($"ActorRef.{field} must be non-empty");
        return s;
    }

    public override void Write(Utf8JsonWriter writer, ActorRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        switch (value)
        {
            case ExprActorRef e:
                writer.WriteString("path", e.Path);
                break;
            case RoleActorRef r:
                writer.WriteString("code", r.Code);
                break;
            case GroupActorRef g:
                writer.WriteString("id", g.Id);
                break;
            case UserActorRef u:
                writer.WriteString("id", u.Id);
                break;
            case ConditionalActorRef c:
                writer.WritePropertyName("condition");
                writer.WriteStartObject();
                writer.WriteString("field", c.Condition.Field);
                writer.WriteString("op", c.Condition.Op);
                writer.WritePropertyName("value");
                c.Condition.Value.WriteTo(writer);
                writer.WriteEndObject();
                writer.WritePropertyName("then");
                Write(writer, c.Then, options);
                writer.WritePropertyName("else");
                Write(writer, c.Else, options);
                break;
            case CollectionActorRef col:
                writer.WriteString("mode", col.Mode);
                if (col.MinApprovals.HasValue) writer.WriteNumber("min_approvals", col.MinApprovals.Value);
                writer.WritePropertyName("actors");
                writer.WriteStartArray();
                foreach (var a in col.Actors) Write(writer, a, options);
                writer.WriteEndArray();
                break;
        }
        if (value.Fallback is not null)
        {
            writer.WritePropertyName("fallback");
            Write(writer, value.Fallback, options);
        }
        writer.WriteEndObject();
    }
}
