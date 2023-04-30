using System;
using Unity.Mathematics;

namespace UnityEngine.BSplines
{
    /// <summary>
    /// Control points for a cubic B-Spline section comprising 4 control points.
    /// The section may not pass through every control point (B-Splines are not interpolating).
    /// </summary>
    public struct BSplineCurve : IEquatable<BSplineCurve>
    {
        /// <summary>
        /// First control point.
        /// </summary>
        public float3 P0;

        /// <summary>
        /// Second control point.
        /// </summary>
        public float3 P1;

        /// <summary>
        /// Third control point.
        /// </summary>
        public float3 P2;

        /// <summary>
        /// Fourth control point.
        /// </summary>
        public float3 P3;

        /// <summary>
        /// Construct a cubic B-Spline section from a series of control points.
        /// </summary>
        /// <param name="p0">The first control point.</param>
        /// <param name="p1">The second control point.</param>
        /// <param name="p2">The third control point.</param>
        /// <param name="p3">The fourth control point.</param>
        public BSplineCurve(float3 p0, float3 p1, float3 p2, float3 p3)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
        }

        public BSplineCurve(ControlPoint c0, ControlPoint c1, ControlPoint c2, ControlPoint c3) :
            this(c0.Position, c1.Position, c2.Position, c3.Position)
        {
        }

        /// <summary>
        /// Multiply the curve positions by a matrix.
        /// </summary>
        /// <param name="matrix">The matrix to multiply.</param>
        /// <returns>A new BSplineCurve multiplied by matrix.</returns>
        public BSplineCurve Transform(float4x4 matrix)
        {
            return new BSplineCurve(
                math.transform(matrix, P0),
                math.transform(matrix, P1),
                math.transform(matrix, P2),
                math.transform(matrix, P3));
        }

        /// <summary>
        /// Gets the same BSplineCurve but in the opposite direction.
        /// </summary>
        /// <returns>Returns the BSplineCurve struct in the inverse direction.</returns>
        public BSplineCurve GetInvertedCurve()
        {
            return new BSplineCurve(P3, P2, P1, P0);
        }

        /// <summary>
        /// Compare two curves for equality.
        /// </summary>
        /// <param name="other">The curve to compare against.</param>
        /// <returns>Returns true when the control points of each curve are identical.</returns>
        public bool Equals(BSplineCurve other)
        {
            return P0.Equals(other.P0) && P1.Equals(other.P1) && P2.Equals(other.P2) && P3.Equals(other.P3);
        }

        /// <summary>
        /// Compare against an object for equality.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        /// Returns true when <paramref name="obj"/> is a <see cref="BSplineCurve"/> and the control points of each
        /// curve are identical.
        /// </returns>
        public override bool Equals(object obj)
        {
            return obj is BSplineCurve other && Equals(other);
        }

        /// <summary>
        /// Calculate a hash code for this curve.
        /// </summary>
        /// <returns>
        /// A hash code for the curve.
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = P0.GetHashCode();
                hashCode = (hashCode * 397) ^ P1.GetHashCode();
                hashCode = (hashCode * 397) ^ P2.GetHashCode();
                hashCode = (hashCode * 397) ^ P3.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>
        /// Compare two curves for equality.
        /// </summary>
        /// <param name="left">The first curve.</param>
        /// <param name="right">The second curve.</param>
        /// <returns>Returns true when the control points of each curve are identical.</returns>
        public static bool operator ==(BSplineCurve left, BSplineCurve right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compare two curves for inequality.
        /// </summary>
        /// <param name="left">The first curve.</param>
        /// <param name="right">The second curve.</param>
        /// <returns>Returns false when the control points of each curve are identical.</returns>
        public static bool operator !=(BSplineCurve left, BSplineCurve right)
        {
            return !left.Equals(right);
        }
    }
}
