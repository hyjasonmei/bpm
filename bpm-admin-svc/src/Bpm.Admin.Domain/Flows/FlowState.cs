using System.Text.Json.Serialization;

namespace Bpm.Admin.Domain.Flows;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlowState
{
    Draft = 0,
    Submitted = 1,
    Cooking = 2,
    OnHold = 3,
    Committed = 4,
    Approved = 5,
    Rejected = 6,
    /// <summary>
    /// Admin has retired this flow version. New case instances are not
    /// allowed (bpm-svc filters retired flows out of the launcher); in-
    /// flight cases continue to terminal. Reversible via UnretireAsync.
    /// </summary>
    Retired = 7,
    /// <summary>
    /// Reviewed (Approved) AND made live in this environment. The bpm-ui
    /// launcher only offers flows in this state. Publish = Approved → Published;
    /// Unpublish = Published → Approved (offline but still reviewed).
    /// </summary>
    Published = 8,
    /// <summary>
    /// Publish has been requested and the cook branch is merged, but the
    /// deploy (build + az) has not yet confirmed success. The flow is NOT
    /// live in the launcher yet. Reached via Publish (Approved/PublishFailed
    /// → Publishing); leaves to Published (MarkPublished) on a green deploy
    /// or PublishFailed (MarkPublishFailed) on a failed one. Appended after
    /// Published so existing persisted int values stay stable.
    /// </summary>
    Publishing = 9,
    /// <summary>
    /// A deploy attempt failed; <see cref="Flow.PublishFailedReason"/> holds
    /// why. Not live in the launcher. Retryable: Publish (PublishFailed →
    /// Publishing) re-attempts the deploy.
    /// </summary>
    PublishFailed = 10,
}
