namespace Bpm.Domain.Workflows;

/// Static description of the PURCHASE BPMN topology — mirror of spec.flow.
/// Behaviour lives on <see cref="Bpm.Domain.Cases.PurchaseCase"/>; this type
/// is the documented transition table the engine and the BPMN view share.
public static class PurchaseWorkflow
{
    public const string FlowCode = "PURCHASE";

    public static readonly IReadOnlyList<PurchaseNode> Nodes = new[]
    {
        new PurchaseNode("start_1",                PurchaseNodeType.StartEvent, "開始"),
        new PurchaseNode("task_request",           PurchaseNodeType.UserTask,   "員工申請"),
        new PurchaseNode("approval_manager",       PurchaseNodeType.Approval,   "主管核准"),
        new PurchaseNode("gateway_after_manager",  PurchaseNodeType.Gateway,    "金額 ≥ 1 萬？"),
        new PurchaseNode("approval_finance",       PurchaseNodeType.Approval,   "財務核准"),
        new PurchaseNode("gateway_after_finance",  PurchaseNodeType.Gateway,    "金額 ≥ 10 萬？"),
        new PurchaseNode("approval_ceo",           PurchaseNodeType.Approval,   "CEO 核准"),
        new PurchaseNode("task_purchase_exec",     PurchaseNodeType.UserTask,   "採購處理"),
        new PurchaseNode("end_1",                  PurchaseNodeType.EndEvent,   "完成"),
    };

    public static readonly IReadOnlyList<PurchaseEdge> Edges = new[]
    {
        new PurchaseEdge("e1",  "start_1",                "task_request",         null,                  false),
        new PurchaseEdge("e2",  "task_request",           "approval_manager",     null,                  false),
        new PurchaseEdge("e3",  "approval_manager",       "gateway_after_manager", null,                 false),
        new PurchaseEdge("e4",  "gateway_after_manager",  "task_purchase_exec",   "amount < 10000",      true),
        new PurchaseEdge("e5",  "gateway_after_manager",  "approval_finance",     "amount >= 10000",     false),
        new PurchaseEdge("e6",  "approval_finance",       "gateway_after_finance", null,                 false),
        new PurchaseEdge("e7",  "gateway_after_finance",  "task_purchase_exec",   "amount < 100000",     true),
        new PurchaseEdge("e8",  "gateway_after_finance",  "approval_ceo",         "amount >= 100000",    false),
        new PurchaseEdge("e9",  "approval_ceo",           "task_purchase_exec",   null,                  false),
        new PurchaseEdge("e10", "task_purchase_exec",     "end_1",                null,                  false),
    };
}

public enum PurchaseNodeType
{
    StartEvent,
    EndEvent,
    UserTask,
    Approval,
    Gateway,
}

public sealed record PurchaseNode(string Id, PurchaseNodeType Type, string Label);

public sealed record PurchaseEdge(string Id, string Source, string Target, string? Condition, bool IsDefault);
