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
}
