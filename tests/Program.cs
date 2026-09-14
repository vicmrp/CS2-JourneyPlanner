using System;
using Colossal.Mathematics;
using CS2_JourneyPlanner;
using Unity.Mathematics;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var straight = new Bezier4x3(new float3(0, 0, 0), new float3(30, 0, 0), new float3(60, 0, 0), new float3(90, 0, 0));
            Check(JourneyGeometry.TryTrim(straight, new float2(0, 1), out var full), "full lane accepted");
            Near(JourneyGeometry.Length(full), 90f, "full lane length");
            Check(JourneyGeometry.TryTrim(straight, new float2(0.25f, 0.75f), out var middle), "partial lane accepted");
            Near(middle.a.x, 22.5f, "partial start");
            Near(middle.d.x, 67.5f, "partial end");
            Near(JourneyGeometry.Length(middle), 45f, "partial distance, not whole lane");
            Check(JourneyGeometry.TryTrim(straight, new float2(0.75f, 0.25f), out var reverse), "reverse lane accepted");
            Near(reverse.a.x, 67.5f, "reverse start");
            Near(reverse.d.x, 22.5f, "reverse end");
            Near(JourneyGeometry.Length(reverse), 45f, "reverse distance remains positive");
            Check(!JourneyGeometry.TryTrim(straight, new float2(0.5f), out _), "zero traversal omitted");
            Check(!JourneyGeometry.TryTrim(straight, new float2(float.NaN, 1f), out _), "NaN rejected");
            Check(!JourneyGeometry.TryTrim(straight, new float2(0f, float.PositiveInfinity), out _), "infinity rejected");
            Check(!JourneyGeometry.TryTrim(straight, new float2(-1f, 1f), out _), "negative interval rejected");
            Check(!JourneyGeometry.TryTrim(straight, new float2(0f, 2f), out _), "out of range interval rejected");
            var bend = new Bezier4x3(new float3(0, 0, 0), new float3(0, 0, 30), new float3(30, 0, 30), new float3(30, 0, 0));
            Check(JourneyGeometry.TryTrim(bend, new float2(0.2f, 0.8f), out var curve), "curved interval accepted");
            Near(math.distance(curve.a, MathUtils.Position(bend, 0.2f)), 0f, "curved start preserved");
            Near(math.distance(curve.d, MathUtils.Position(bend, 0.8f)), 0f, "curved end preserved");
            Check(JourneyGeometry.Length(curve) > math.distance(curve.a, curve.d), "curve is not replaced with a straight shortcut");
            Console.WriteLine("PASS: 19 geometry assertions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private static void Near(float actual, float expected, string name) => Check(Math.Abs(actual - expected) < 0.001f, name + ": " + actual + " != " + expected);
}
