using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.BSplines;
using Object = UnityEngine.Object;

namespace UnityEditor.BSplines
{
    static class TransformOperation
    {
        [Flags]
        public enum PivotFreeze
        {
            None = 0,
            Position = 1,
            Rotation = 2,
            All = Position | Rotation
        }

        struct TransformData
        {
            internal float3 position;

            internal static TransformData GetData(ISplineElement element)
            {
                var tData = new TransformData();
                tData.position = new float3(element.Position);
                var knot = new SelectableKnot(element.SplineInfo, element.KnotIndex);

                return tData;
            }
        }

        struct RotationSyncData
        {
            quaternion m_RotationDelta;
            float m_MagnitudeDelta;
            float m_ScaleMultiplier; // Only used for scale operation
            bool m_Initialized;

            public bool initialized => m_Initialized;
            public quaternion rotationDelta => m_RotationDelta;
            public float magnitudeDelta => m_MagnitudeDelta;
            public float scaleMultiplier => m_ScaleMultiplier;

            public void Initialize(quaternion rotationDelta, float magnitudeDelta, float scaleMultiplier)
            {
                m_RotationDelta = rotationDelta;
                m_MagnitudeDelta = magnitudeDelta;
                m_ScaleMultiplier = scaleMultiplier;
                m_Initialized = true;
            }

            public void Clear()
            {
                m_RotationDelta = quaternion.identity;
                m_MagnitudeDelta = 0f;
                m_ScaleMultiplier = 1f;
                m_Initialized = false;
            }
        }

        static readonly List<ISplineElement> s_ElementSelection = new List<ISplineElement>(32);

        public static IReadOnlyList<ISplineElement> elementSelection => s_ElementSelection;

        static int s_ElementSelectionCount = 0;

        public static bool canManipulate => s_ElementSelectionCount > 0;

        public static ISplineElement currentElementSelected
            => canManipulate ? s_ElementSelection[0] : null;

        static Vector3 s_PivotPosition;
        public static Vector3 pivotPosition => s_PivotPosition;

        static quaternion s_HandleRotation;
        public static quaternion handleRotation => s_HandleRotation;

        //Caching rotation inverse for rotate and scale operations
        static quaternion s_HandleRotationInv;

        public static PivotFreeze pivotFreeze { get; set; }

        static TransformData[] s_MouseDownData;

        // Used to prevent same knot being rotated multiple times during a transform operation in Rotation Sync mode.
        static HashSet<SelectableKnot> s_RotatedKnotCache = new HashSet<SelectableKnot>();

        // Used to prevent the translation of the same knot multiple times if a linked knot was moved
        static HashSet<SelectableKnot> s_LinkedKnotCache = new HashSet<SelectableKnot>();

        static readonly List<SelectableKnot> s_KnotBuffer = new List<SelectableKnot>();
        static RotationSyncData s_RotationSyncData = new RotationSyncData();

        internal static void UpdateSelection(IEnumerable<Object> selection)
        {
            SplineSelection.GetElements(EditorSplineUtility.GetSplinesFromTargetsInternal(selection), s_ElementSelection);
            s_ElementSelectionCount = s_ElementSelection.Count;
            if (s_ElementSelectionCount > 0)
            {
                UpdatePivotPosition();
                UpdateHandleRotation();
            }
        }

        internal static void UpdatePivotPosition(bool useKnotPositionForTangents = false)
        {
            if ((pivotFreeze & PivotFreeze.Position) != 0)
                return;

            switch (Tools.pivotMode)
            {
                case PivotMode.Center:
                    s_PivotPosition = EditorSplineUtility.GetElementBounds(s_ElementSelection, useKnotPositionForTangents).center;
                    break;

                case PivotMode.Pivot:
                    if (s_ElementSelectionCount == 0)
                        goto default;

                    var element = s_ElementSelection[0];
                    s_PivotPosition = element.Position;
                    break;

                default:
                    s_PivotPosition = Vector3.positiveInfinity;
                    break;
            }
        }

        // A way to set pivot position for situations, when by design, pivot position does
        // not necessarily match the pivot of selected elements.
        internal static void ForcePivotPosition(float3 position)
        {
            s_PivotPosition = position;
        }

        internal static void UpdateHandleRotation()
        {
            if ((pivotFreeze & PivotFreeze.Rotation) != 0)
                return;

            var handleRotation = Tools.handleRotation;

            s_HandleRotation = handleRotation;
            s_HandleRotationInv = math.inverse(s_HandleRotation);
        }

        public static void ApplyTranslation(float3 delta)
        {
            s_RotatedKnotCache.Clear();
            s_LinkedKnotCache.Clear();

            foreach (var element in s_ElementSelection)
            {
                if (element is SelectableKnot knot)
                {
                    if (!s_LinkedKnotCache.Contains(knot))
                    {
                        knot.Position = ApplySmartRounding(knot.Position + delta);

                        EditorSplineUtility.GetKnotLinks(knot, s_KnotBuffer);
                        foreach (var k in s_KnotBuffer)
                            s_LinkedKnotCache.Add(k);

                        if (!s_RotationSyncData.initialized)
                            s_RotationSyncData.Initialize(quaternion.identity, 0f, 1f);
                    }
                }
            }

            s_RotationSyncData.Clear();
        }

        public static void ApplyScale(float3 scale)
        {
            s_RotatedKnotCache.Clear();
            ISplineElement[] scaledElements = new ISplineElement[s_ElementSelectionCount];

            for (int elementIndex = 0; elementIndex < s_ElementSelectionCount; elementIndex++)
            {
                var element = s_ElementSelection[elementIndex];
                if (element is SelectableKnot knot)
                {
                    ScaleKnot(knot, elementIndex, scale);

                    if (!s_RotationSyncData.initialized)
                        s_RotationSyncData.Initialize(quaternion.identity, 0f, 1f);
                }

                scaledElements[elementIndex] = element;
            }

            s_RotationSyncData.Clear();
        }

        static void ScaleKnot(SelectableKnot knot, int dataIndex, float3 scale)
        {
            if (Tools.pivotMode == PivotMode.Center)
            {
                var deltaPos = math.rotate(s_HandleRotationInv,
                    s_MouseDownData[dataIndex].position - (float3) pivotPosition);
                var deltaPosKnot = deltaPos * scale;
                knot.Position = math.rotate(s_HandleRotation, deltaPosKnot) + (float3) pivotPosition;
            }
        }

        static SelectableKnot GetCurrentSelectionKnot()
        {
            if (currentElementSelected == null)
                return default;

            if (currentElementSelected is SelectableKnot knot)
                return knot;

            return default;
        }

        public static void RecordMouseDownState()
        {
            s_MouseDownData = new TransformData[s_ElementSelectionCount];
            for (int i = 0; i < s_ElementSelectionCount; i++)
            {
                s_MouseDownData[i] = TransformData.GetData(s_ElementSelection[i]);
            }
        }

        public static void ClearMouseDownState()
        {
            s_MouseDownData = null;
        }

        public static Bounds GetSelectionBounds(bool useKnotPositionForTangents = false)
        {
            return EditorSplineUtility.GetElementBounds(s_ElementSelection, useKnotPositionForTangents);
        }

        public static float3 ApplySmartRounding(float3 position)
        {
            //If we are snapping, disable the smart rounding. If not the case, the transform will have the wrong snap value based on distance to screen.
#if UNITY_2022_2_OR_NEWER
            if (EditorSnapSettings.incrementalSnapActive || EditorSnapSettings.gridSnapActive)
                return position;
#endif

            float3 minDifference = SplineHandleUtility.GetMinDifference(position);
            for (int i = 0; i < 3; ++i)
                position[i] = Mathf.Approximately(position[i], 0f) ? position[i] : SplineHandleUtility.RoundBasedOnMinimumDifference(position[i], minDifference[i]);

            return position;
        }
    }
}