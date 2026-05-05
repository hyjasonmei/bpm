using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bpm.Domain.Spec;

/// Discriminated union of every shape spec.json may use to refer to an actor
/// (approver, assignee, notify recipient). Polymorphic deserialisation is
/// driven by the `type` field — see ActorRefJsonConverter.
[JsonConverter(typeof(ActorRefJsonConverter))]
public abstract record ActorRef
{
    public ActorRef? Fallback { get; init; }

    public abstract string Type { get; }
}

public sealed record ExprActorRef(string Path) : ActorRef
{
    public override string Type => "expr";
}

public sealed record RoleActorRef(string Code) : ActorRef
{
    public override string Type => "role";
}

public sealed record GroupActorRef(string Id) : ActorRef
{
    public override string Type => "group";
}

public sealed record UserActorRef(string Id) : ActorRef
{
    public override string Type => "user";
}

public sealed record ConditionalActorRef(
    ActorRefCondition Condition,
    ActorRef Then,
    ActorRef Else) : ActorRef
{
    public override string Type => "conditional";
}

public sealed record CollectionActorRef(
    string Mode,             // "any" or "all"
    IReadOnlyList<ActorRef> Actors,
    int? MinApprovals = null) : ActorRef
{
    public override string Type => "collection";
}

public sealed record ActorRefCondition(
    string Field,
    string Op,               // ==, !=, >, >=, <, <=, in, not_in
    JsonElement Value);
