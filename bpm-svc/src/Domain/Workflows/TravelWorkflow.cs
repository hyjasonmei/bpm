namespace Bpm.Domain.Workflows;

public static class TravelWorkflow
{
    public const string FlowCode = "TRAVEL";

    public static readonly IReadOnlyList<TravelNode> Nodes = new[]
    {
        new TravelNode("start_1",          TravelNodeType.StartEvent, "開始"),
        new TravelNode("task_request",     TravelNodeType.UserTask,   "員工申請"),
        new TravelNode("approval_manager", TravelNodeType.Approval,   "主管核准"),
        new TravelNode("gateway_intl",     TravelNodeType.Gateway,    "是否國外？"),
        new TravelNode("approval_vp",      TravelNodeType.Approval,   "副總核准"),
        new TravelNode("task_admin_book",  TravelNodeType.UserTask,   "行政訂票"),
        new TravelNode("end_1",            TravelNodeType.EndEvent,   "完成"),
    };

    public static readonly IReadOnlyList<TravelEdge> Edges = new[]
    {
        new TravelEdge("e1", "start_1",          "task_request",     null,                                  false),
        new TravelEdge("e2", "task_request",     "approval_manager", null,                                  false),
        new TravelEdge("e3", "approval_manager", "gateway_intl",     null,                                  false),
        new TravelEdge("e4", "gateway_intl",     "task_admin_book",  "destinationType == 'domestic'",       true),
        new TravelEdge("e5", "gateway_intl",     "approval_vp",      "destinationType == 'international'",  false),
        new TravelEdge("e6", "approval_vp",      "task_admin_book",  null,                                  false),
        new TravelEdge("e7", "task_admin_book",  "end_1",            null,                                  false),
    };
}

public enum TravelNodeType { StartEvent, EndEvent, UserTask, Approval, Gateway }

public sealed record TravelNode(string Id, TravelNodeType Type, string Label);
public sealed record TravelEdge(string Id, string Source, string Target, string? Condition, bool IsDefault);
