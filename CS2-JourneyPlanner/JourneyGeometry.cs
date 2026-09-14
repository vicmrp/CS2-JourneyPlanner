using Colossal.Mathematics;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    internal static class JourneyGeometry
    {
        // Native path elements may cover only part of a lane, in either direction.
        internal static bool TryTrim(Bezier4x3 source, float2 interval, out Bezier4x3 curve)
        {
            curve = source;
            if (!math.all(math.isfinite(interval)) || math.any(interval < 0f) || math.any(interval > 1f)) return false;
            if (math.abs(interval.y - interval.x) < 0.00001f) return false;
            curve = MathUtils.Cut(source, interval);
            return true;
        }

        internal static float Length(Bezier4x3 curve, int samples = 14)
        {
            float distance = 0f;
            float3 previous = curve.a;
            for (int i = 1; i <= samples; i++)
            {
                float3 current = MathUtils.Position(curve, i / (float)samples);
                distance += math.distance(previous, current);
                previous = current;
            }
            return distance;
        }
    }
}
