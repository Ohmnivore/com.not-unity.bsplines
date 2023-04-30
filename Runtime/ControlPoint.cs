using System;
using Unity.Mathematics;

namespace UnityEngine.BSplines
{
    /// <summary>
    /// This struct contains data for a B-Spline control point. The position is a scalar point and the tangents are vectors.
    /// The <see cref="Spline"/> class stores a collection of ControlPoint that form a B-Spline.
    /// Each point contains a Position.
    /// </summary>
    [Serializable]
    public struct ControlPoint : IEquatable<ControlPoint>
    {
        /// <summary>
        /// The position of the point.
        /// </summary>
        public float3 Position;

        /// <summary>
        /// Create a new ControlPoint struct.
        /// </summary>
        /// <param name="position">The position of the point relative to the spline.</param>
        public ControlPoint(float3 position)
        {
            Position = position;
        }

        /// <summary>
        /// Multiply the position by a matrix.
        /// </summary>
        /// <param name="matrix">The matrix to multiply.</param>
        /// <returns>A new ControlPoint multiplied by matrix.</returns>
        public ControlPoint Transform(float4x4 matrix)
        {
            return new ControlPoint(math.transform(matrix, Position));
        }

        /// <summary>
        /// Get a mirrored version of this point.
        /// </summary>
        /// <param name="mirrorPoint">The point to mirror around.</param>
        /// <returns>A new ControlPoint which represents this ControlPoint mirrored around another ControlPoint.</returns>
        public ControlPoint GetMirrorAround(ControlPoint mirrorPoint)
        {
            var delta = mirrorPoint.Position - Position;

            return new ControlPoint(mirrorPoint.Position + delta);
        }

        /// <summary>
        /// Position addition.
        /// </summary>
        /// <param name="point">The target point.</param>
        /// <param name="rhs">The value to add.</param>
        /// <returns>A new ControlPoint where position is the sum of point.position and rhs.</returns>
        public static ControlPoint operator +(ControlPoint point, float3 rhs)
        {
            return new ControlPoint(point.Position + rhs);
        }

        /// <summary>
        /// Position subtraction.
        /// </summary>
        /// <param name="point">The target point.</param>
        /// <param name="rhs">The value to subtract.</param>
        /// <returns>A new ControlPoint where position is the sum of point.position minus rhs.</returns>
        public static ControlPoint operator -(ControlPoint point, float3 rhs)
        {
            return new ControlPoint(point.Position - rhs);
        }

        /// <summary>
        /// Create a string with the values of this point.
        /// </summary>
        /// <returns>A summary of the values contained by this point.</returns>
        public override string ToString() => $"{{{Position}}}";

        /// <summary>
        /// Compare two points for equality.
        /// </summary>
        /// <param name="other">The point to compare against.</param>
        /// <returns>Returns true when the position, tangents, and rotation of each point are identical.</returns>
        public bool Equals(ControlPoint other)
        {
            return Position.Equals(other.Position);
        }

        /// <summary>
        /// Compare against an object for equality.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        /// Returns true when <paramref name="obj"/> is a <see cref="ControlPoint"/> and the values of each point are
        /// identical.
        /// </returns>
        public override bool Equals(object obj)
        {
            return obj is ControlPoint other && Equals(other);
        }

        /// <summary>
        /// Calculate a hash code for this point.
        /// </summary>
        /// <returns>
        /// A hash code for the point.
        /// </returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Position);
        }
    }
}
