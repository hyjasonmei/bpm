using Cel.Checker;
using Cel.Tools;
using Cel.Common.Types.Json;
using CelType = Google.Api.Expr.V1Alpha1.Type;

// Spike: assess whether Cel.NET (rayokota, NuGet 1.0.0) covers the bpm-cel-v1 subset.
// Run: dotnet run --project spikes/cel-net-spike/CelNetSpike

var pass = 0;
var fail = 0;
var notes = new List<string>();

void Test(string label, Func<bool> body)
{
    try
    {
        var result = body();
        if (result) { Console.WriteLine($"  PASS  {label}"); pass++; }
        else { Console.WriteLine($"  FAIL  {label} — returned false"); fail++; notes.Add($"{label}: returned false"); }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  {label} — {ex.GetType().Name}: {ex.Message}");
        fail++;
        notes.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
    }
}

ScriptHost host = ScriptHost.NewBuilder().Registry(JsonRegistry.NewRegistry()).Build();

bool Eval(string expr, IDictionary<string, object> values, params (string name, CelType type)[] decls)
{
    var builder = host.BuildScript(expr);
    foreach (var (name, type) in decls)
    {
        builder.WithDeclarations(Decls.NewVar(name, type));
    }
    var script = builder.Build();
    return script.Execute<bool>(values);
}

T EvalAs<T>(string expr, IDictionary<string, object> values, params (string name, CelType type)[] decls)
{
    var builder = host.BuildScript(expr);
    foreach (var (name, type) in decls)
    {
        builder.WithDeclarations(Decls.NewVar(name, type));
    }
    var script = builder.Build();
    return script.Execute<T>(values);
}

Probe.FunctionProbe.Run();

Console.WriteLine("\n=== bpm-cel-v1 spike ===\n");

Console.WriteLine("[1] Basic comparison operators");
Test("== string", () => Eval("leave_type == '病假'", new Dictionary<string, object> { ["leave_type"] = "病假" }, ("leave_type", Decls.String)));
Test("!= int", () => Eval("status != 0", new Dictionary<string, object> { ["status"] = 1L }, ("status", Decls.Int)));
Test("> int", () => Eval("amount > 50000", new Dictionary<string, object> { ["amount"] = 80000L }, ("amount", Decls.Int)));
Test(">= double", () => Eval("rate >= 0.05", new Dictionary<string, object> { ["rate"] = 0.07 }, ("rate", Decls.Double)));

Console.WriteLine("\n[2] Logical operators");
Test("&&", () => Eval("a && b", new Dictionary<string, object> { ["a"] = true, ["b"] = true }, ("a", Decls.Bool), ("b", Decls.Bool)));
Test("||", () => Eval("a || b", new Dictionary<string, object> { ["a"] = false, ["b"] = true }, ("a", Decls.Bool), ("b", Decls.Bool)));
Test("!", () => Eval("!a", new Dictionary<string, object> { ["a"] = false }, ("a", Decls.Bool)));

Console.WriteLine("\n[3] Arithmetic");
Test("+ int", () => EvalAs<long>("a + b", new Dictionary<string, object> { ["a"] = 5L, ["b"] = 3L }, ("a", Decls.Int), ("b", Decls.Int)) == 8L);
Test("* int", () => EvalAs<long>("quantity * unit_price", new Dictionary<string, object> { ["quantity"] = 5L, ["unit_price"] = 100L }, ("quantity", Decls.Int), ("unit_price", Decls.Int)) == 500L);
Test("/ int", () => EvalAs<long>("a / b", new Dictionary<string, object> { ["a"] = 10L, ["b"] = 2L }, ("a", Decls.Int), ("b", Decls.Int)) == 5L);
Test("% int", () => EvalAs<long>("a % b", new Dictionary<string, object> { ["a"] = 7L, ["b"] = 3L }, ("a", Decls.Int), ("b", Decls.Int)) == 1L);
Test("/ by zero throws", () => { try { EvalAs<long>("a / b", new Dictionary<string, object> { ["a"] = 10L, ["b"] = 0L }, ("a", Decls.Int), ("b", Decls.Int)); return false; } catch { return true; } });

Console.WriteLine("\n[4] Ternary");
Test("?:", () => EvalAs<long>("amount > 50000 ? 1 : 0", new Dictionary<string, object> { ["amount"] = 80000L }, ("amount", Decls.Int)) == 1L);

Console.WriteLine("\n[5] Membership");
Test("in list", () => Eval("'病假' in ['特休', '病假', '事假']", new Dictionary<string, object>()));

Console.WriteLine("\n[6] String built-ins (CEL standard)");
Test("size(string)", () => EvalAs<long>("size(reason)", new Dictionary<string, object> { ["reason"] = "需要看醫生" }, ("reason", Decls.String)) == 5L);
Test("string.matches (regex)", () => Eval("email.matches('^[^@]+@.*\\\\..+$')", new Dictionary<string, object> { ["email"] = "wilson@acme.com" }, ("email", Decls.String)));
Test("string.startsWith", () => Eval("dept.startsWith('finance')", new Dictionary<string, object> { ["dept"] = "finance-tw" }, ("dept", Decls.String)));
Test("string.endsWith", () => Eval("file.endsWith('.pdf')", new Dictionary<string, object> { ["file"] = "report.pdf" }, ("file", Decls.String)));
Test("string.contains", () => Eval("name.contains('Wilson')", new Dictionary<string, object> { ["name"] = "Wilson You" }, ("name", Decls.String)));

Console.WriteLine("\n[7] List built-ins");
Test("size(list)", () => EvalAs<long>("size(items)", new Dictionary<string, object> { ["items"] = new List<long> { 1, 2, 3, 4 } }, ("items", Decls.NewListType(Decls.Int))) == 4L);

Console.WriteLine("\n[8] Macros (we declared them OUT of bpm-cel-v1; verify they exist so we can detect/reject)");
Test("exists() macro", () => Eval("[1, 2, 3].exists(x, x > 2)", new Dictionary<string, object>()));
Test("filter() macro", () => EvalAs<long>("size([1, 2, 3].filter(x, x > 1))", new Dictionary<string, object>()) == 2L);
Test("map() macro", () => EvalAs<long>("size([1, 2, 3].map(x, x * 2))", new Dictionary<string, object>()) == 3L);
Test("all() macro", () => Eval("[1, 2, 3].all(x, x > 0)", new Dictionary<string, object>()));

Console.WriteLine("\n[9] Object property access (Json.NET registered)");

string scriptDecl = "inp.Email == 'wilson@acme.com'";
try
{
    var script = host.BuildScript(scriptDecl)
        .WithDeclarations(Decls.NewVar("inp", Decls.NewObjectType(typeof(SubmitterDto).FullName!)))
        .WithTypes(typeof(SubmitterDto))
        .Build();
    var inpArgs = new Dictionary<string, object> { ["inp"] = new SubmitterDto { Email = "wilson@acme.com", Name = "Wilson", Dept = "engineering" } };
    Test("inp.Email property access", () => script.Execute<bool>(inpArgs));
}
catch (Exception ex)
{
    Console.WriteLine($"  FAIL  inp.Email property access — setup threw {ex.GetType().Name}: {ex.Message}");
    fail++;
    notes.Add($"object access setup failed: {ex.Message}");
}

Console.WriteLine("\n[10] Real spec expressions from bpm domain");
Test("conditional: 病假 needs cert", () => Eval("leave_type == '病假'", new Dictionary<string, object> { ["leave_type"] = "病假" }, ("leave_type", Decls.String)));
Test("gateway: VP if amount > 50K", () => Eval("amount > 50000", new Dictionary<string, object> { ["amount"] = 80000L }, ("amount", Decls.Int)));
Test("derived: total = qty * price", () => EvalAs<long>("quantity * unit_price", new Dictionary<string, object> { ["quantity"] = 5L, ["unit_price"] = 100L }, ("quantity", Decls.Int), ("unit_price", Decls.Int)) == 500L);
Test("validator: email regex", () => Eval("email.matches('^[^@]+@.*\\\\..+$')", new Dictionary<string, object> { ["email"] = "wilson@acme.com" }, ("email", Decls.String)));
Test("len > N: reason at least 10 chars", () => Eval("size(reason) >= 10", new Dictionary<string, object> { ["reason"] = "需要好好休息一下身體" }, ("reason", Decls.String)));

Console.WriteLine("\n[11] Custom functions check (notes — see report)");
Console.WriteLine("  Standard CEL provides: matches, contains, startsWith, endsWith, size,");
Console.WriteLine("  in, +, -, *, /, %, ==, !=, <, <=, >, >=, &&, ||, !, ?:, list literals,");
Console.WriteLine("  exists/filter/map (macros), timestamp, duration arithmetic.");
Console.WriteLine("  Custom needed: sum, lower, upper, now, today, daysBetween, businessDaysBetween");

Console.WriteLine($"\n=== Spike result: {pass} pass / {fail} fail ===\n");

if (notes.Count > 0)
{
    Console.WriteLine("Failures detail:");
    foreach (var n in notes) Console.WriteLine($"  - {n}");
}

public class SubmitterDto
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Dept { get; set; } = "";
}
