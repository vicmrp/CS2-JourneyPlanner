using Colossal.Mathematics;
using Game;
using Game.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2_JourneyPlanner
{
    // Geometry is prepared once per calculation. Rendering only submits the cached
    // curves to the game's overlay buffer, with no GameObjects or Materials.
    public sealed partial class JourneyPlannerRouteSystem : GameSystemBase
    {
        internal struct RouteCurve
        {
            public Bezier4x3 Curve;
            public Color Color;
            public float Width;
            public int LegIndex;
        }

        internal struct StopMarker
        {
            public float3 Position;
            public Color Color;
            public int CurveIndex;
            public int LegIndex;
        }

        private OverlayRenderSystem _overlay;
        private CameraUpdateSystem _camera;
        private NativeList<RouteCurve> _curves;
        private NativeList<StopMarker> _stops;
        internal bool Visible = true;
        private int _firstCurve;
        private float _firstFraction;

        protected override void OnCreate()
        {
            base.OnCreate();
            _overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            _curves = new NativeList<RouteCurve>(256, Allocator.Persistent);
            _stops = new NativeList<StopMarker>(32, Allocator.Persistent);
        }

        internal void ClearRoute()
        {
            Dependency.Complete();
            if (!_curves.IsCreated || !_stops.IsCreated) return;
            _curves.Clear();
            _stops.Clear();
            _firstCurve = 0;
            _firstFraction = 0f;
        }

        // Add calls occur synchronously after ClearRoute, before a new render job.
        internal void AddCurve(RouteCurve curve) => _curves.Add(curve);
        internal void AddStop(StopMarker stop) => _stops.Add(stop);

        internal void SetProgress(int curve, float fraction)
        {
            _firstCurve = curve;
            _firstFraction = math.clamp(fraction, 0f, 1f);
        }

        internal void SetLegColor(int legIndex, Color color)
        {
            Dependency.Complete();
            for (int i = 0; i < _curves.Length; i++)
            {
                RouteCurve curve = _curves[i];
                if (curve.LegIndex != legIndex) continue;
                curve.Color = color;
                _curves[i] = curve;
            }
            for (int i = 0; i < _stops.Length; i++)
            {
                StopMarker stop = _stops[i];
                if (stop.LegIndex != legIndex) continue;
                stop.Color = color;
                _stops[i] = stop;
            }
        }

        protected override void OnUpdate()
        {
            if (!Visible || _curves.Length == 0 || _firstCurve >= _curves.Length) return;
            var buffer = _overlay.GetBuffer(out JobHandle overlayDependency);
            float zoom = math.saturate((_camera.zoom - 1600f) / 8400f);
            Dependency = new DrawRouteJob
            {
                Buffer = buffer,
                Curves = _curves.AsArray(),
                Stops = _stops.AsArray(),
                FirstCurve = _firstCurve,
                FirstFraction = _firstFraction,
                WidthScale = math.lerp(1f, 12f, zoom)
            }.Schedule(JobHandle.CombineDependencies(Dependency, overlayDependency));
            _overlay.AddBufferWriter(Dependency);
        }

        [BurstCompile]
        private struct DrawRouteJob : IJob
        {
            public OverlayRenderSystem.Buffer Buffer;
            [ReadOnly] public NativeArray<RouteCurve> Curves;
            [ReadOnly] public NativeArray<StopMarker> Stops;
            public int FirstCurve;
            public float FirstFraction;
            public float WidthScale;

            public void Execute()
            {
                for (int i = FirstCurve; i < Curves.Length; i++)
                {
                    RouteCurve item = Curves[i];
                    Bezier4x3 curve = i == FirstCurve && FirstFraction > 0f
                        ? MathUtils.Cut(item.Curve, new float2(FirstFraction, 1f)) : item.Curve;
                    Buffer.DrawCurve(item.Color, curve, item.Width * WidthScale, new float2(0f, 1f));
                }
                for (int i = 0; i < Stops.Length; i++)
                {
                    StopMarker stop = Stops[i];
                    if (stop.CurveIndex < FirstCurve || (stop.CurveIndex == FirstCurve && FirstFraction > 0f)) continue;
                    Buffer.DrawCircle(stop.Color, Color.white, 2f * WidthScale,
                        (OverlayRenderSystem.StyleFlags)0, new float2(0f, 1f), stop.Position, 10f * WidthScale);
                }
            }
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            _curves.Dispose();
            _stops.Dispose();
            base.OnDestroy();
        }
    }
}
