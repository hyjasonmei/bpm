using Cel;
using Cel.Checker;
using Cel.Common.Types;
using Cel.Common.Types.Ref;
using Cel.Interpreter.Functions;
using NodaTime;
using IClock = Bpm.Application.Common.Abstractions.IClock;

namespace Bpm.Application.Spec.Expressions;

/// <summary>
/// bpm-cel-v1 custom functions registered as a CEL library.
/// Standard CEL covers operators / comparisons / size / matches / contains
/// natively — this library adds the 7 functions CEL spec doesn't include:
/// now, today, daysBetween, businessDaysBetween, sum, lower, upper.
/// </summary>
public sealed class BpmCelLibrary(IClock clock) : ILibrary
{
    public IList<EnvOption> CompileOptions => new List<EnvOption>
    {
        EnvOptions.Declarations(
            Decls.NewFunction("now",
                Decls.NewOverload("now_void", new List<Google.Api.Expr.V1Alpha1.Type>(), Decls.Timestamp)),
            Decls.NewFunction("today",
                Decls.NewOverload("today_void", new List<Google.Api.Expr.V1Alpha1.Type>(), Decls.Timestamp)),
            Decls.NewFunction("daysBetween",
                Decls.NewOverload("daysBetween_ts_ts", new List<Google.Api.Expr.V1Alpha1.Type> { Decls.Timestamp, Decls.Timestamp }, Decls.Int)),
            Decls.NewFunction("businessDaysBetween",
                Decls.NewOverload("businessDaysBetween_ts_ts", new List<Google.Api.Expr.V1Alpha1.Type> { Decls.Timestamp, Decls.Timestamp }, Decls.Int)),
            Decls.NewFunction("sum",
                Decls.NewOverload("sum_list_int", new List<Google.Api.Expr.V1Alpha1.Type> { Decls.NewListType(Decls.Int) }, Decls.Int),
                Decls.NewOverload("sum_list_double", new List<Google.Api.Expr.V1Alpha1.Type> { Decls.NewListType(Decls.Double) }, Decls.Double)),
            Decls.NewFunction("lower",
                Decls.NewOverload("lower_str", new List<Google.Api.Expr.V1Alpha1.Type> { Decls.String }, Decls.String)),
            Decls.NewFunction("upper",
                Decls.NewOverload("upper_str", new List<Google.Api.Expr.V1Alpha1.Type> { Decls.String }, Decls.String))
        )
    };

    public IList<ProgramOption> ProgramOptions => new List<ProgramOption>
    {
        global::Cel.ProgramOptions.Functions(
            Overload.Function("now", _ => TimestampT.TimestampOf(Instant.FromDateTimeUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)))),
            Overload.Function("today", _ =>
            {
                var startOfDayLocal = clock.TodayInTaipei().ToDateTime(TimeOnly.MinValue);
                var asUtc = DateTime.SpecifyKind(startOfDayLocal, DateTimeKind.Utc);
                return TimestampT.TimestampOf(Instant.FromDateTimeUtc(asUtc));
            }),
            Overload.Function("daysBetween", args =>
            {
                var t1 = ExtractInstant(args[0]);
                var t2 = ExtractInstant(args[1]);
                var span = (long)(t2 - t1).TotalDays;
                return IntT.IntOf(span);
            }),
            Overload.Function("businessDaysBetween", args =>
            {
                // v1 stub: returns natural days. Will be replaced with calendar-aware
                // computation once add-calendar-and-business-hours ships.
                var t1 = ExtractInstant(args[0]);
                var t2 = ExtractInstant(args[1]);
                var span = (long)(t2 - t1).TotalDays;
                return IntT.IntOf(span);
            }),
            Overload.Function("sum", args =>
            {
                var raw = ((IVal)args[0]).Value();
                long iSum = 0;
                double dSum = 0;
                bool isDouble = false;
                if (raw is System.Collections.IEnumerable seq)
                {
                    foreach (var v in seq)
                    {
                        switch (v)
                        {
                            case long l: iSum += l; break;
                            case int i: iSum += i; break;
                            case double d: dSum += d; isDouble = true; break;
                            case float f: dSum += f; isDouble = true; break;
                            case decimal m: dSum += (double)m; isDouble = true; break;
                            case IntT it: iSum += (long)it.Value(); break;
                            case DoubleT dt: dSum += (double)dt.Value(); isDouble = true; break;
                            default: throw new InvalidOperationException($"sum: unsupported element type {v?.GetType().Name ?? "null"}");
                        }
                    }
                }
                return isDouble ? DoubleT.DoubleOf(dSum + iSum) : IntT.IntOf(iSum);
            }),
            Overload.Function("lower", args =>
            {
                var s = (string)((StringT)args[0]).Value();
                return StringT.StringOf(s.ToLowerInvariant());
            }),
            Overload.Function("upper", args =>
            {
                var s = (string)((StringT)args[0]).Value();
                return StringT.StringOf(s.ToUpperInvariant());
            })
        )
    };

    private static Instant ExtractInstant(IVal v)
    {
        var raw = v.Value();
        return raw switch
        {
            Instant i => i,
            ZonedDateTime z => z.ToInstant(),
            DateTimeOffset dto => Instant.FromDateTimeOffset(dto),
            DateTime dt => Instant.FromDateTimeUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"cannot extract Instant from {raw?.GetType().Name ?? "null"}"),
        };
    }
}
