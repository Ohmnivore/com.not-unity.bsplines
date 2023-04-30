using System;
using Unity.Mathematics;
using UnityEngine.BSplines;

namespace UnityEditor.BSplines
{
    interface ISplineElement : IEquatable<ISplineElement>
    {
        SplineInfo SplineInfo { get; }
        int KnotIndex { get; }
        float3 LocalPosition { get; set; }
        float3 Position { get; set; }
        bool IsValid();
    }

    struct SelectableKnot : ISplineElement, IEquatable<SelectableKnot>
    {
        public SplineInfo SplineInfo { get; }
        public int KnotIndex { get; }

        public float4x4 LocalToWorld => SplineInfo.LocalToWorld;

        public float3 Position
        {
            get => math.transform(LocalToWorld, LocalPosition);
            set => LocalPosition = math.transform(math.inverse(LocalToWorld), value);
        }

        public float3 LocalPosition
        {
            get => SplineInfo.Spline[KnotIndex].Position;
            set
            {
                var knot = SplineInfo.Spline[KnotIndex];
                knot.Position = value;
                SplineInfo.Spline[KnotIndex] = knot;
            }
        }

        public bool IsValid()
        {
            return SplineInfo.Spline != null && KnotIndex >= 0 && KnotIndex < SplineInfo.Spline.Count;
        }

        public SelectableKnot(SplineInfo info, int index)
        {
            this.SplineInfo = info;
            this.KnotIndex = index;
        }

        public ControlPoint GetBezierKnot(bool worldSpace)
        {
            return worldSpace ? SplineInfo.Spline[KnotIndex].Transform(LocalToWorld) : SplineInfo.Spline[KnotIndex];
        }

        public bool Equals(ISplineElement other)
        {
            if (other is SelectableKnot knot)
                return Equals(knot);
            return false;
        }

        public bool Equals(SelectableKnot other)
        {
            return Equals(SplineInfo.Spline, other.SplineInfo.Spline) && KnotIndex == other.KnotIndex;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is SelectableKnot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SplineInfo.Spline, KnotIndex);
        }
    }
}
