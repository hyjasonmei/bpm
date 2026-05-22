using System.Reflection;

namespace Probe;

public static class FunctionProbe
{
    public static void Run()
    {
        var asm = typeof(Cel.Tools.ScriptHost).Assembly;
        Console.WriteLine($"\n=== Cel.NET assembly types matching Decl/Func ===");
        foreach (var t in asm.ExportedTypes.Where(x => (x.Name.Contains("Decl") || x.Name.Contains("Func") || x.Name.Contains("Overload")) && !x.IsNested))
            Console.WriteLine($"  {t.FullName}");

        Console.WriteLine($"\n=== Decls public statics ===");
        foreach (var f in typeof(Cel.Checker.Decls).GetFields(BindingFlags.Public | BindingFlags.Static))
            Console.WriteLine($"  field: {f.FieldType.Name} {f.Name}");
        foreach (var p in typeof(Cel.Checker.Decls).GetProperties(BindingFlags.Public | BindingFlags.Static))
            Console.WriteLine($"  prop:  {p.PropertyType.Name} {p.Name}");

        Console.WriteLine($"\n=== TimestampT.TimestampOf overloads ===");
        foreach (var m in typeof(Cel.Common.Types.TimestampT).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == "TimestampOf"))
            Console.WriteLine($"  {m.ReturnType.Name} TimestampOf({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");

        Console.WriteLine($"\n=== Cel.ProgramOptions vs ProgramOptions ===");
        Console.WriteLine($"  Cel.ProgramOptions full: {typeof(global::Cel.ProgramOptions).FullName}");
        foreach (var m in typeof(global::Cel.ProgramOptions).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name == "Functions"))
            Console.WriteLine($"  Functions({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
    }
}
