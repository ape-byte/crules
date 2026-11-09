using System.Linq.Expressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
var i = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LambdaExpressions).EnableAssignment(AssignmentOperators.None);
var ctx = Expression.Parameter(typeof(Ctx), "ctx");
var total = Expression.Variable(typeof(decimal), "total");
var ps = new[] { new Parameter(ctx), new Parameter(total) };
Expression P(string s, Type? type = null) => type is null ? i.Parse(s, ps).Expression : i.Parse(s, type, ps).Expression;
int failures = 0;
void Check(string id, Func<bool> run) { try { bool passed = run(); if (!passed) failures++; Console.WriteLine($"{id}: {(passed ? "PASS" : "FAIL")}"); } catch(Exception e) { failures++; Console.WriteLine($"{id}: ERROR {e.GetType().Name}: {e.Message}"); } }
Check("P01 parameter identity", () => ReferenceEquals(P("total"), total));
Check("P02 inferred decimal", () => P("ctx.Amount * 0.8m").Type == typeof(decimal));
Check("P03 block assignment branch return", () => {
 var ret = Expression.Label(typeof(decimal));
 var body = Expression.Block(new[]{ total }, Expression.Assign(total, P("ctx.Amount * 0.8m")), Expression.IfThen(P("ctx.IsVip", typeof(bool)), Expression.Block(Expression.Assign(total, P("total * 0.9m", typeof(decimal))), Expression.Empty())), Expression.Return(ret, total), Expression.Label(ret, Expression.Default(typeof(decimal))));
 var f = Expression.Lambda<Func<Ctx,decimal>>(body,ctx).Compile();
 return f(new(100m,true,[1m,2m])) == 72m && f(new(100m,false,[])) == 80m;
});
Check("P04 LINQ captures mutable local", () => {
 var sum = P("ctx.Items.Where(x => x > total).Sum()", typeof(decimal));
 var body = Expression.Block(new[]{total}, Expression.Assign(total,Expression.Constant(2m)), sum);
 var f = Expression.Lambda<Func<Ctx,decimal>>(body,ctx).Compile();
 return f(new(0,false,[1m,3m,4m])) == 7m;
});
Check("P05 expected decimal converts int", () => P("1",typeof(decimal)).Type == typeof(decimal));
Check("P06 assignment expression disabled", () => { try { P("total = 1m"); return false; } catch(ParseException) { return true; } });
Check("P07 error offset", () => { try { P("total + missing"); return false; } catch(ParseException e) { Console.WriteLine($"offset={e.Position}; message={e.Message}"); return e.Position == 8; } });
Check("P08 SetExpression local binding", () => ReferenceEquals(new Interpreter().SetExpression("v", total).Parse("v").Expression, total));
Check("P09 decimal rounding", () => {
 var e = i.Reference(typeof(MidpointRounding)).Parse("Math.Round(1.005m, 2, MidpointRounding.ToEven)");
 return Expression.Lambda<Func<decimal>>(e.Expression).Compile()() == 1m;
});
Check("P10 nested lambda parameter same name", () => {
 try { P("ctx.Items.Where(total => total > 1m).Sum()"); return false; } catch(ParseException) { return true; }
});
Console.WriteLine($"Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; Package={typeof(Interpreter).Assembly.FullName}");
Environment.ExitCode = failures == 0 ? 0 : 1;
public record Ctx(decimal Amount, bool IsVip, decimal[] Items);
