using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.BSplines;
using Unity.Mathematics;
using UnityEditor.SettingsManagement;
using UnityEditor.ShortcutManagement;
using Object = UnityEngine.Object;

#if UNITY_2022_1_OR_NEWER
using UnityEditor.Overlays;

#else
using System.Reflection;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;
#endif

namespace UnityEditor.BSplines
{
    [CustomEditor(typeof(KnotPlacementTool))]
#if UNITY_2022_1_OR_NEWER
    class KnotPlacementToolSettings : UnityEditor.Editor, ICreateToolbar
    {
        public IEnumerable<string> toolbarElements
        {
#else
    class KnotPlacementToolSettings : CreateToolbarBase
    {
        protected override IEnumerable<string> toolbarElements
        {
#endif
            get { yield return "Spline Tool Settings/Default Knot Type"; }
        }
    }

    [EditorTool("Draw Spline", typeof(ISplineContainer), typeof(SplineToolContext))]
    sealed class KnotPlacementTool : SplineTool
    {   
        // 6f is the threshold used in RectSelection, but felt a little too sensitive when drawing a path.
        const float k_MinDragThreshold = 8f;

        [UserSetting("Knot Placement", "Drag to Set Tangent Length", "When placing new knots, click then drag to adjust" +
            " the length and direction of the tangents. Disable this option to always place auto-smooth knots.")]
        static Pref<bool> s_EnableDragTangent = new ($"{nameof(KnotPlacementHandle)}.{nameof(s_EnableDragTangent)}", true);

        static readonly string k_KnotPlacementUndoMessage = L10n.Tr("Add Spline Knot");
        
        sealed class DrawingOperation : IDisposable
        {
            /// <summary>
            /// Indicates whether the knot placed is at the start or end of a curve segment
            /// </summary>
            public enum DrawingDirection
            {
                Start,
                End
            }

            public bool HasStartedDrawing { get; private set; }

            public DrawingDirection Direction
            {
                get => m_Direction;
            }

            public readonly SplineInfo CurrentSplineInfo;

            readonly DrawingDirection m_Direction;
            readonly bool m_AllowDeleteIfNoCurves;

            /// <summary>
            /// Gets the last index of the knot on the spline
            /// </summary>
            /// <returns>The index of the last knot on the spline - this will be the same as the starting knot
            /// in a closed spline</returns>
            int GetLastKnotIndex()
            {
                var isFromStartAndClosed = m_Direction == DrawingDirection.Start && CurrentSplineInfo.Spline.Closed;
                var isFromEndAndOpened = m_Direction == DrawingDirection.End && !CurrentSplineInfo.Spline.Closed;
                return isFromStartAndClosed || isFromEndAndOpened ? ( CurrentSplineInfo.Spline.Count - 1 ) : 0;
            }

            internal SelectableKnot GetLastAddedKnot()
            {
                return new SelectableKnot(CurrentSplineInfo, GetLastKnotIndex());
            }

            public DrawingOperation(SplineInfo splineInfo, DrawingDirection direction, bool allowDeleteIfNoCurves)
            {
                CurrentSplineInfo = splineInfo;
                m_Direction = direction;
                m_AllowDeleteIfNoCurves = allowDeleteIfNoCurves;
            }

            public void OnGUI(IReadOnlyList<SplineInfo> splines)
            {
                KnotPlacementHandle(splines, this, CreateKnotOnKnot, CreateKnotOnSurface, DrawCurvePreview);
            }

            void UncloseSplineIfNeeded()
            {
                // If the spline was closed, we unclose it, create a knot on the last knot and connect the first and last
                if (CurrentSplineInfo.Spline.Closed)
                {
                    CurrentSplineInfo.Spline.Closed = false;

                    switch (m_Direction)
                    {
                        case DrawingDirection.Start:
                        {
                            var lastKnot = new SelectableKnot(CurrentSplineInfo, CurrentSplineInfo.Spline.Count - 1);
                            EditorSplineUtility.AddKnotToTheStart(CurrentSplineInfo, lastKnot.Position);

                            //Adding the knot before the first element is shifting indexes using a callback
                            //Using a delay called here to be certain that the indexes has been shift and that this new link won't be shifted
                            EditorApplication.delayCall += () =>
                                EditorSplineUtility.LinkKnots(new SelectableKnot(CurrentSplineInfo, 1),
                                    new SelectableKnot(CurrentSplineInfo, CurrentSplineInfo.Spline.Count - 1));
                            break;
                        }

                        case DrawingDirection.End:
                        {
                            var firstKnot = new SelectableKnot(CurrentSplineInfo, 0);
                            EditorSplineUtility.AddKnotToTheEnd(CurrentSplineInfo, firstKnot.Position);
                            EditorSplineUtility.LinkKnots(new SelectableKnot(CurrentSplineInfo, 0),
                                new SelectableKnot(CurrentSplineInfo, CurrentSplineInfo.Spline.Count - 1));
                            break;
                        }
                    }
                }
            }
            
            internal void CreateKnotOnKnot(SelectableKnot knot)
            {
                EditorSplineUtility.RecordObject(CurrentSplineInfo, k_KnotPlacementUndoMessage);

                var lastAddedKnot = GetLastAddedKnot();
                if (knot.Equals(lastAddedKnot))
                    return;

                // If the user clicks on the first knot (or a knot linked to the first knot) of the spline close the spline
                var closeKnotIndex = m_Direction == DrawingDirection.End ? 0 : knot.SplineInfo.Spline.Count - 1;
                if (knot.SplineInfo.Equals(CurrentSplineInfo)
                    && ( knot.KnotIndex == closeKnotIndex ||
                         EditorSplineUtility.AreKnotLinked(knot,
                             new SelectableKnot(CurrentSplineInfo, closeKnotIndex)) ))
                {
                    knot.SplineInfo.Spline.Closed = true;
                }
                else
                {
                    UncloseSplineIfNeeded();

                    lastAddedKnot = AddKnot(knot.Position);
                    if (m_Direction == DrawingDirection.End || knot.SplineInfo.Index != lastAddedKnot.SplineInfo.Index)
                        EditorSplineUtility.LinkKnots(knot, lastAddedKnot);
                    else
                        EditorSplineUtility.LinkKnots(new SelectableKnot(knot.SplineInfo, knot.KnotIndex + 1),
                            lastAddedKnot);
                    
                    // Already called in AddKnot but this is not recording the updated Linkedknots in that case
                    PrefabUtility.RecordPrefabInstancePropertyModifications(knot.SplineInfo.Object);
                }
                
            }

            internal void CreateKnotOnSurface(float3 position)
            {
                EditorSplineUtility.RecordObject(CurrentSplineInfo, k_KnotPlacementUndoMessage);

                var lastKnot = GetLastAddedKnot();

                if (lastKnot.IsValid())
                    position = ApplyIncrementalSnap(position, lastKnot.Position);

                UncloseSplineIfNeeded();

                AddKnot(position);
            }

            SelectableKnot AddKnot(float3 position)
            {
                switch (m_Direction)
                {
                    case DrawingDirection.Start:
                        return EditorSplineUtility.AddKnotToTheStart(CurrentSplineInfo, position, false);

                    case DrawingDirection.End:
                        return EditorSplineUtility.AddKnotToTheEnd(CurrentSplineInfo, position, false);
                }

                return default;
            }

            void DrawCurvePreview(float3 position, float3 normal, float3 tangent, SelectableKnot target)
            {
                var lastKnot = GetLastAddedKnot();
                if (target.IsValid() && target.Equals(lastKnot))
                    return;

                position = ApplyIncrementalSnap(position, lastKnot.Position);

                BSplineCurve previewCurve = m_Direction == DrawingDirection.Start
                    ? EditorSplineUtility.GetPreviewCurveFromStart(CurrentSplineInfo, lastKnot.KnotIndex, position)
                    : EditorSplineUtility.GetPreviewCurveFromEnd(CurrentSplineInfo, lastKnot.KnotIndex, position);

                CurveHandles.Draw(-1, previewCurve);

#if UNITY_2022_2_OR_NEWER
                KnotHandles.Draw(position, SplineUtility.GetKnotRotation(tangent, normal), Handles.elementColor, false, false);
#else
                KnotHandles.Draw(position, SplineUtility.GetKnotRotation(tangent, normal), SplineHandleUtility.knotColor, false, false);
#endif
            }

            /// <summary>
            /// Remove drawing action that created no curves and were canceled after being created
            /// </summary>
            public void Dispose()
            {
                var spline = CurrentSplineInfo.Spline;

                if (m_AllowDeleteIfNoCurves && spline != null && spline.Count == 1)
                {
                    EditorSplineUtility.RecordObject(CurrentSplineInfo, "Removing Empty Spline");
                    CurrentSplineInfo.Container.RemoveSplineAt(CurrentSplineInfo.Index);
                }
            }
        }

#if UNITY_2022_2_OR_NEWER
        public override bool gridSnapEnabled => true;
#endif

        public override GUIContent toolbarIcon => PathIcons.knotPlacementTool;

        static bool IsMouseInWindow(EditorWindow window) => new Rect(Vector2.zero, window.position.size).Contains(Event.current.mousePosition);

        static PlacementData s_PlacementData;

        static SplineInfo s_ClosestSpline = default;

        int m_ActiveObjectIndex = 0;
        readonly List<Object> m_SortedTargets = new List<Object>();
        readonly List<SplineInfo> m_SplineBuffer = new List<SplineInfo>(4);
        static readonly List<SelectableKnot> s_KnotsBuffer = new List<SelectableKnot>();
        DrawingOperation m_CurrentDrawingOperation;
        Object m_MainTarget;
        //Needed for Tests
        internal Object MainTarget
        {
            get => m_MainTarget;
            set => m_MainTarget = value;
        }

        public override void OnActivated()
        {
            base.OnActivated();
            SplineToolContext.UseCustomSplineHandles(true);
            SplineSelection.Clear();
            SplineSelection.UpdateObjectSelection(targets);
            m_ActiveObjectIndex = 0;
        }

        public override void OnWillBeDeactivated()
        {
            base.OnWillBeDeactivated();
            SplineToolContext.UseCustomSplineHandles(false);
            EndDrawingOperation();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var targets = GetSortedTargets(out m_MainTarget);
            var allSplines = EditorSplineUtility.GetSplinesFromTargetsInternal(targets);

            //If the spline being drawn on doesn't exist anymore, end the drawing operation
            if (m_CurrentDrawingOperation != null &&
                ( !allSplines.Contains(m_CurrentDrawingOperation.CurrentSplineInfo) ||
                  m_CurrentDrawingOperation.CurrentSplineInfo.Spline.Count == 0 ))
                EndDrawingOperation();

            DrawSplines(targets, allSplines, m_MainTarget);

            if (m_CurrentDrawingOperation == null)
                KnotPlacementHandle(allSplines, null, AddKnotOnKnot, AddKnotOnSurface, DrawKnotCreationPreview);
            else
                m_CurrentDrawingOperation.OnGUI(allSplines);

            HandleCancellation();
        }

        // Curve id to SelectableKnotList - if we're inserting on a curve, we need 3 knots to preview the change, for other cases it's 2 knots
        internal static List<(Spline spline, int curveIndex, (ControlPoint p0, ControlPoint p1, ControlPoint p2, ControlPoint p3) knots)> previewCurvesList = new();

        void DrawSplines(IReadOnlyList<Object> targets, IReadOnlyList<SplineInfo> allSplines, Object mainTarget)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EditorSplineUtility.TryGetNearestKnot(allSplines, out SelectableKnot hoveredKnot);

            foreach (var target in targets)
            {
                EditorSplineUtility.GetSplinesFromTarget(target, m_SplineBuffer);
                bool isMainTarget = target == mainTarget;

                var previewIndex = 0;


                //Draw curves
                foreach (var splineInfo in m_SplineBuffer)
                {
                    var spline = splineInfo.Spline;
                    var localToWorld = splineInfo.LocalToWorld;

                    for (int i = 0, count = spline.GetCurveCount(); i < count; ++i)
                    {
                        if (previewIndex < previewCurvesList.Count)
                        {
                            var currentPreview = previewCurvesList[previewIndex];

                            if (currentPreview.spline.Equals(spline) && currentPreview.curveIndex == i)
                            {
                                var curveKnots = currentPreview.knots;
                                var previewCurve = new BSplineCurve(curveKnots.p0, curveKnots.p1, curveKnots.p2, curveKnots.p3);
                                previewCurve = previewCurve.Transform(localToWorld);
                                CurveHandles.Draw(previewCurve, isMainTarget);
                                if (isMainTarget)
                                {
                                    CurveHandles.DrawFlow(
                                        previewCurve,
                                        null,
                                        -1,
                                        Vector3.up,
                                        Vector3.up);
                                }

                                previewIndex++;
                                continue;
                            }
                        }

                        var curve = spline.GetCurve(i).Transform(localToWorld);
                        CurveHandles.Draw(curve, isMainTarget);
                        if (isMainTarget)
                        {
                            CurveHandles.DrawFlow(
                                curve,
                                splineInfo.Spline,
                                i,
                                Vector3.up,
                                Vector3.up);
                        }
                    }
                }

                //Draw control polygon
                foreach (var splineInfo in m_SplineBuffer)
                {
                    SplineHandles.DrawSplineControlPolygon(splineInfo);
                }

                //Draw knots
                foreach (var splineInfo in m_SplineBuffer)
                {
                    var spline = splineInfo.Spline;

                    for (int knotIndex = 0, count = spline.Count; knotIndex < count; ++knotIndex)
                    {
                        bool isHovered = hoveredKnot.SplineInfo.Equals(splineInfo) &&
                                         hoveredKnot.KnotIndex == knotIndex;
                        if (isMainTarget || isHovered)
                        {
#if UNITY_2022_2_OR_NEWER
                            KnotHandles.Draw(new SelectableKnot(splineInfo, knotIndex), Handles.elementColor, false, isHovered);
#else
                            KnotHandles.Draw(new SelectableKnot(splineInfo, knotIndex), SplineHandleUtility.knotColor, false, isHovered);
#endif
                        }
                        else
                            KnotHandles.DrawInformativeKnot(new SelectableKnot(splineInfo, knotIndex));
                    }
                }
            }
        }

        void AddKnotOnKnot(SelectableKnot startFrom)
        {
            Undo.RecordObject(startFrom.SplineInfo.Object, k_KnotPlacementUndoMessage);

            EndDrawingOperation();

            m_ActiveObjectIndex = GetTargetIndex(startFrom.SplineInfo);

            // If we start from one of the ends of the spline we just append to that spline unless
            // the spline is already closed or there is other links knots.
            EditorSplineUtility.GetKnotLinks(startFrom, s_KnotsBuffer);
            if (s_KnotsBuffer.Count == 1 && !startFrom.SplineInfo.Spline.Closed)
            {
                if (EditorSplineUtility.IsEndKnot(startFrom))
                {
                    m_CurrentDrawingOperation = new DrawingOperation(startFrom.SplineInfo,
                        DrawingOperation.DrawingDirection.End, false);
                    
                    return;
                }

                if (startFrom.KnotIndex == 0)
                {
                    m_CurrentDrawingOperation = new DrawingOperation(startFrom.SplineInfo,
                        DrawingOperation.DrawingDirection.Start, false);
                    
                    return;
                }
            }

            // Otherwise we start a new spline
            var knot = EditorSplineUtility.CreateSpline(startFrom);
            EditorSplineUtility.LinkKnots(knot, startFrom);
            m_CurrentDrawingOperation =
                new DrawingOperation(knot.SplineInfo, DrawingOperation.DrawingDirection.End, true);
        }

        void AddKnotOnSurface(float3 position)
        {
            Undo.RecordObject(m_MainTarget, k_KnotPlacementUndoMessage);

            EndDrawingOperation();

            var container = (ISplineContainer)m_MainTarget;

            // Check component count to ensure that we only move the transform of a newly created
            // spline. I.e., we don't want to move a GameObject that has other components like
            // a MeshRenderer, for example.
            if (( container.Splines.Count == 1 && container.Splines[0].Count == 0
                  || container.Splines.Count == 0 )
                && ( (Component)m_MainTarget ).GetComponents<Component>().Length == 2)
            {
                ( (Component)m_MainTarget ).transform.position = position;
            }

            SplineInfo splineInfo;

            // Spline gets created with an empty spline so we add to that spline first if needed
            if (container.Splines.Count == 1 && container.Splines[0].Count == 0)
                splineInfo = new SplineInfo(container, 0);
            else
                splineInfo = EditorSplineUtility.CreateSpline(container);

            EditorSplineUtility.AddKnotToTheEnd(splineInfo, position, false);
            m_CurrentDrawingOperation = new DrawingOperation(splineInfo, DrawingOperation.DrawingDirection.End, false);
        }

        //SelectableKnot is not used and only here as this method is used as a `Action<float3, float3, float3, SelectableKnot>` by the `KnotPlacementHandle` method
        void DrawKnotCreationPreview(float3 position, float3 normal, float3 tangentOut, SelectableKnot _)
        {
#if UNITY_2022_2_OR_NEWER
            KnotHandles.Draw(position, SplineUtility.GetKnotRotation(tangentOut, normal), Handles.elementColor, false, false);
#else
            KnotHandles.Draw(position, SplineUtility.GetKnotRotation(tangentOut, normal), SplineHandleUtility.knotColor, false, false);
#endif
        }

        static void KnotPlacementHandle(
            IReadOnlyList<SplineInfo> splines,
            DrawingOperation drawingOperation,
            Action<SelectableKnot> createKnotOnKnot,
            Action<float3> createKnotOnSurface,
            Action<float3, float3, float3, SelectableKnot> drawPreview)
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            var evt = Event.current;

            if (s_PlacementData != null && GUIUtility.hotControl != controlId)
                s_PlacementData = null;

            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    if (!Tools.viewToolActive)
                        HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.Repaint:
                {
                    var mousePosition = Event.current.mousePosition;
                    if (GUIUtility.hotControl == 0
                        && SceneView.currentDrawingSceneView != null
                        && !IsMouseInWindow(SceneView.currentDrawingSceneView))
                        break;

                    if (GUIUtility.hotControl == 0)
                    {
                        if (EditorSplineUtility.TryGetNearestKnot(splines, out SelectableKnot knot))
                        {
                            drawPreview.Invoke(knot.Position, math.rotate(Quaternion.identity, math.up()), float3.zero, knot);
                        }
                        else if (EditorSplineUtility.TryGetNearestPositionOnCurve(splines, out SplineCurveHit hit))
                        {
                            drawPreview.Invoke(hit.Position, hit.Normal, float3.zero, default);
                        }
                        else if (SplineHandleUtility.GetPointOnSurfaces(mousePosition, out Vector3 position,
                                     out Vector3 normal))
                        {
                            drawPreview.Invoke(position, normal, float3.zero, default);
                        }
                    }

                    if (s_PlacementData != null)
                    {
                        var knotPosition = s_PlacementData.Position;
                        var tangentOut = s_PlacementData.TangentOut;

                        drawPreview.Invoke(s_PlacementData.Position, s_PlacementData.Normal, s_PlacementData.TangentOut,
                            default);
                    }

                    break;
                }

                case EventType.MouseMove:
                    var mouseMovePosition = Event.current.mousePosition;
                    previewCurvesList.Clear();

                    s_ClosestSpline = default;
                    var hasNearKnot = EditorSplineUtility.TryGetNearestKnot(splines, out SelectableKnot k);
                    if (hasNearKnot)
                        s_ClosestSpline = k.SplineInfo;

                    if (SplineHandleUtility.GetPointOnSurfaces(mouseMovePosition, out Vector3 pos, out Vector3 _))
                    {
                        if (drawingOperation != null)
                        {
                            var lastKnot = drawingOperation.GetLastAddedKnot();
                            var previousKnotIndex = drawingOperation.Direction == DrawingOperation.DrawingDirection.End
                                ? drawingOperation.CurrentSplineInfo.Spline.PreviousIndex(lastKnot.KnotIndex)
                                : drawingOperation.CurrentSplineInfo.Spline.NextIndex(lastKnot.KnotIndex);

                            EditorSplineUtility.GetAffectedCurves(
                                drawingOperation.CurrentSplineInfo,
                                drawingOperation.CurrentSplineInfo.Transform.InverseTransformPoint(pos),
                                lastKnot, previousKnotIndex, previewCurvesList);
                        }
                    }

                    if (HandleUtility.nearestControl == controlId)
                        HandleUtility.Repaint();

                    break;

                case EventType.MouseDown:
                {
                    if (evt.button != 0 || Tools.viewToolActive)
                        break;

                    if (HandleUtility.nearestControl == controlId)
                    {
                        GUIUtility.hotControl = controlId;
                        evt.Use();

                        var mousePosition = Event.current.mousePosition;
                        if (EditorSplineUtility.TryGetNearestKnot(splines, out SelectableKnot knot))
                        {
                            s_PlacementData = new KnotPlacementData(evt.mousePosition, knot);
                        }
                        else if (EditorSplineUtility.TryGetNearestPositionOnCurve(splines, out SplineCurveHit hit))
                        {
                            s_PlacementData = new CurvePlacementData(evt.mousePosition, hit);
                        }
                        else if (SplineHandleUtility.GetPointOnSurfaces(mousePosition, out Vector3 position,
                                     out Vector3 normal))
                        {
                            s_PlacementData = new PlacementData(evt.mousePosition, position, normal);
                        }
                    }

                    break;
                }

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && evt.button == 0)
                    {
                        evt.Use();

                        if (s_PlacementData != null
                            && s_EnableDragTangent
                            && Vector3.Distance(evt.mousePosition, s_PlacementData.MousePosition) > k_MinDragThreshold)
                        {
                            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                            if (s_PlacementData.Plane.Raycast(ray, out float distance))
                            {
                                s_PlacementData.TangentOut =
                                    ( ray.origin + ray.direction * distance ) - s_PlacementData.Position;
                            }
                        }
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && evt.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();

                        if (s_PlacementData != null)
                        {
                            var linkedKnot = s_PlacementData.GetOrCreateLinkedKnot();
                            if (linkedKnot.IsValid())
                                createKnotOnKnot.Invoke(linkedKnot);
                            else
                                createKnotOnSurface.Invoke(s_PlacementData.Position);

                            s_PlacementData = null;
                            previewCurvesList.Clear();
                        }
                    }

                    break;

                case EventType.KeyDown:
                    if (GUIUtility.hotControl == controlId &&
                        ( evt.keyCode == KeyCode.Escape || evt.keyCode == KeyCode.Return ))
                    {
                        s_PlacementData = null;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }

                    break;
            }
        }

        void HandleCancellation()
        {
            var evt = Event.current;
            if (GUIUtility.hotControl == 0 && evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Return)
                {
                    //If we are currently drawing, end the drawing operation and start a new one. If we haven't started drawing, switch to move tool instead
                    if (m_CurrentDrawingOperation != null)
                        EndDrawingOperation();
                    else
                        ToolManager.SetActiveTool<SplineMoveTool>();
                }

#if !UNITY_2022_1_OR_NEWER
                //For 2022.1 and after the ESC key is handled in the EditorToolManager
                if (evt.keyCode == KeyCode.Escape)
                    ToolManager.SetActiveTool<SplineMoveTool>();
#endif
            }
        }

        internal void EndDrawingOperation()
        {
            m_CurrentDrawingOperation?.Dispose();
            m_CurrentDrawingOperation = null;
        }

        int GetTargetIndex(SplineInfo info)
        {
            return targets.ToList().IndexOf(info.Object);
        }

        IReadOnlyList<Object> GetSortedTargets(out Object mainTarget)
        {
            m_SortedTargets.Clear();
            m_SortedTargets.AddRange(targets);

            if (m_ActiveObjectIndex >= m_SortedTargets.Count)
                m_ActiveObjectIndex = 0;

            mainTarget = m_SortedTargets[m_ActiveObjectIndex];
            if (m_CurrentDrawingOperation != null)
                mainTarget = m_CurrentDrawingOperation.CurrentSplineInfo.Object;
            else if (!s_ClosestSpline.Equals(default))
                mainTarget = s_ClosestSpline.Object;

            // Move main target to the end for rendering/picking
            m_SortedTargets.Remove(mainTarget);
            m_SortedTargets.Add(mainTarget);

            return m_SortedTargets;
        }

        static Vector3 ApplyIncrementalSnap(Vector3 current, Vector3 origin)
        {
#if UNITY_2022_2_OR_NEWER
            if (EditorSnapSettings.incrementalSnapActive)
                return SplineHandleUtility.DoIncrementSnap(current, origin);
#endif
            return current;
        }

        void CycleActiveTarget()
        {
            m_ActiveObjectIndex = ( m_ActiveObjectIndex + 1 ) % targets.Count();
            SceneView.RepaintAll();
        }

        [Shortcut("B-Splines/Cycle Active Spline", typeof(SceneView), KeyCode.S)]
        static void ShortcutCycleActiveSpline(ShortcutArguments args)
        {
            if (activeTool is KnotPlacementTool tool)
                tool.CycleActiveTarget();
        }
        
        /// <summary>
        /// Used for tests
        /// </summary>
        internal void AddKnotOnSurfaceInternal(Vector3 position, bool endDrawing = false)
        {
            if (m_CurrentDrawingOperation == null)
                AddKnotOnSurface(position);
            else
                m_CurrentDrawingOperation.CreateKnotOnSurface(position);
            
            if(endDrawing)
                EndDrawingOperation();
        }
        /// <summary>
        /// Used for tests
        /// </summary>
        internal void AddKnotOnKnotInternal(int splineIndex, int knotIndex, bool endDrawing = false)
        {
            var fromSplineInfo = new SplineInfo(MainTarget as SplineContainer, splineIndex);
            if (m_CurrentDrawingOperation == null)
                AddKnotOnKnot(new SelectableKnot(fromSplineInfo, knotIndex));
            else
                m_CurrentDrawingOperation.CreateKnotOnKnot(new SelectableKnot(fromSplineInfo, knotIndex));
            
            if(endDrawing)
                EndDrawingOperation();
        }
    }
}
