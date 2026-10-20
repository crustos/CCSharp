// UnityEngine's UI: RectTransform (Unity's layout: a rectangle placed by anchors in its parent's, sized by sizeDelta, positioned by its pivot), Canvas
// (a root canvas is sized and placed from the screen every frame, scaled by its CanvasScaler: in screen pixels for an overlay, before its camera for
// ScreenSpaceCamera), CanvasGroup; UnityEngine.EventSystems (selection, the handler interfaces, ExecuteEvents); and UnityEngine.UI (Graphic, Image,
// RawImage, Text, Selectable with its colour tints, Button, Toggle and ToggleGroup, Slider, Scrollbar, ScrollRect, InputField, the layout
// components).
//
// Not here yet: drawing (the host draws sprites only), the pointer side of the event system (a game that hit-tests its own UI, as Slime Jump does,
// works; IsPointerOverGameObject is false), keyboard and gamepad navigation, the layout groups' layout (the scene holds the rectangles Unity laid
// out, which the packer writes).

using System;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityEngine
{
    public class RectTransform : Transform
    {
        public enum Edge { Left = 0, Right = 1, Top = 2, Bottom = 3 }
        public enum Axis { Horizontal = 0, Vertical = 1 }

        Vector2 m_AnchorMin = new Vector2(0.5f, 0.5f);
        Vector2 m_AnchorMax = new Vector2(0.5f, 0.5f);
        Vector2 m_SizeDelta = new Vector2(100f, 100f);
        Vector2 m_Pivot = new Vector2(0.5f, 0.5f);

        static float Get(Vector2 v, int axis) { return axis == 0 ? v.x : v.y; }
        static Vector2 With(Vector2 v, int axis, float value) { if (axis == 0) v.x = value; else v.y = value; return v; }
        static Vector2 Mul(Vector2 a, Vector2 b) { return new Vector2(a.x * b.x, a.y * b.y); }

        /// <summary>The parent's rectangle (in the parent's space); a parent that is not a RectTransform has none.</summary>
        Rect ParentRect()
        {
            RectTransform p = parent as RectTransform;
            return p != null ? p.rect : new Rect(0f, 0f, 0f, 0f);
        }

        Vector2 AnchorSize(Rect pr) { return Mul(m_AnchorMax - m_AnchorMin, pr.size); }

        /// <summary>The point of the parent's rectangle that anchoredPosition is measured from: the anchor box, at this rectangle's pivot.</summary>
        Vector2 AnchorReference(Rect pr)
        {
            Vector2 amin = pr.min + Mul(m_AnchorMin, pr.size);
            Vector2 amax = pr.min + Mul(m_AnchorMax, pr.size);
            return amin + Mul(amax - amin, m_Pivot);
        }

        /// <summary>A change to the rectangle keeps the anchoredPosition of it and of everything below it, as Unity's does (so a stretched child
        /// follows its parent).</summary>
        void ChangeRect(Action change)
        {
            List<RectTransform> all = new List<RectTransform>();
            List<Vector2> at = new List<Vector2>();
            Collect(this, all, at);
            change();
            for (int i = 0; i < all.Count; i++) all[i].SetAnchoredPositionRaw(at[i]);
        }

        static void Collect(RectTransform r, List<RectTransform> all, List<Vector2> at)
        {
            all.Add(r);
            at.Add(r.anchoredPosition);
            for (int i = 0; i < r.childCount; i++)
            {
                RectTransform c = r.GetChild(i) as RectTransform;
                if (c != null) Collect(c, all, at);
            }
        }

        void SetAnchoredPositionRaw(Vector2 value)
        {
            Vector2 r = AnchorReference(ParentRect());
            localPosition = new Vector3(r.x + value.x, r.y + value.y, localPosition.z);
        }

        public Vector2 anchorMin { get { return m_AnchorMin; } set { ChangeRect(() => m_AnchorMin = value); } }
        public Vector2 anchorMax { get { return m_AnchorMax; } set { ChangeRect(() => m_AnchorMax = value); } }
        public Vector2 pivot { get { return m_Pivot; } set { ChangeRect(() => m_Pivot = value); } }
        public Vector2 sizeDelta { get { return m_SizeDelta; } set { ChangeRect(() => m_SizeDelta = value); } }

        public Vector2 anchoredPosition
        {
            get
            {
                Vector2 r = AnchorReference(ParentRect());
                return new Vector2(localPosition.x - r.x, localPosition.y - r.y);
            }
            set { SetAnchoredPositionRaw(value); }
        }

        public Vector3 anchoredPosition3D
        {
            get { Vector2 a = anchoredPosition; return new Vector3(a.x, a.y, localPosition.z); }
            set { anchoredPosition = new Vector2(value.x, value.y); localPosition = new Vector3(localPosition.x, localPosition.y, value.z); }
        }

        /// <summary>The rectangle in this transform's own space (its pivot at the origin).</summary>
        public Rect rect
        {
            get
            {
                Vector2 size = AnchorSize(ParentRect()) + m_SizeDelta;
                return new Rect(-m_Pivot.x * size.x, -m_Pivot.y * size.y, size.x, size.y);
            }
        }

        public Vector2 offsetMin
        {
            get { return anchoredPosition - Mul(sizeDelta, pivot); }
            set
            {
                Vector2 offset = value - (anchoredPosition - Mul(sizeDelta, pivot));
                Vector2 ap = anchoredPosition;
                ChangeRect(() => m_SizeDelta = m_SizeDelta - offset);
                anchoredPosition = ap + Mul(offset, Vector2.one - pivot);
            }
        }

        public Vector2 offsetMax
        {
            get { return anchoredPosition + Mul(sizeDelta, Vector2.one - pivot); }
            set
            {
                Vector2 offset = value - (anchoredPosition + Mul(sizeDelta, Vector2.one - pivot));
                Vector2 ap = anchoredPosition;
                ChangeRect(() => m_SizeDelta = m_SizeDelta + offset);
                anchoredPosition = ap + Mul(offset, pivot);
            }
        }

        /// <summary>Bottom left, top left, top right, bottom right, in this transform's space.</summary>
        public void GetLocalCorners(Vector3[] fourCornersArray)
        {
            Rect r = rect;
            fourCornersArray[0] = new Vector3(r.xMin, r.yMin, 0f);
            fourCornersArray[1] = new Vector3(r.xMin, r.yMax, 0f);
            fourCornersArray[2] = new Vector3(r.xMax, r.yMax, 0f);
            fourCornersArray[3] = new Vector3(r.xMax, r.yMin, 0f);
        }

        public void GetWorldCorners(Vector3[] fourCornersArray)
        {
            GetLocalCorners(fourCornersArray);
            for (int i = 0; i < 4; i++) fourCornersArray[i] = TransformPoint(fourCornersArray[i]);
        }

        public void SetSizeWithCurrentAnchors(Axis axis, float size)
        {
            int a = (int)axis;
            Vector2 parentSize = AnchorSize(ParentRect());
            sizeDelta = With(m_SizeDelta, a, size - Get(parentSize, a));
        }

        public void SetInsetAndSizeFromParentEdge(Edge edge, float inset, float size)
        {
            int axis = (edge == Edge.Top || edge == Edge.Bottom) ? 1 : 0;
            bool end = edge == Edge.Top || edge == Edge.Right;
            float anchor = end ? 1f : 0f;
            anchorMin = With(m_AnchorMin, axis, anchor);
            anchorMax = With(m_AnchorMax, axis, anchor);
            sizeDelta = With(m_SizeDelta, axis, size);
            Vector2 p = anchoredPosition;
            anchoredPosition = With(p, axis, end ? -inset - size * (1f - Get(m_Pivot, axis)) : inset + size * Get(m_Pivot, axis));
        }

        public void ForceUpdateRectTransforms() { }

        /// <summary>A root canvas's rectangle, driven from the screen (Canvas does it each frame): a size, and its place in the world.</summary>
        internal void DriveRoot(Vector2 size, Vector3 worldPosition, Quaternion worldRotation, float worldScale)
        {
            ChangeRect(() =>
            {
                m_AnchorMin = Vector2.zero;
                m_AnchorMax = Vector2.zero;
                m_SizeDelta = size;
                m_Pivot = new Vector2(0.5f, 0.5f);
                position = worldPosition;
                rotation = worldRotation;
                localScale = new Vector3(worldScale, worldScale, worldScale);
            });
        }
    }

    public static class RectTransformUtility
    {
        /// <summary>A screen point in the world, on the rectangle's plane (the camera's view for a camera canvas, screen pixels for an overlay).</summary>
        public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector3 worldPoint)
        {
            worldPoint = cam == null ? new Vector3(screenPoint.x, screenPoint.y, rect.position.z) : cam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, 0f));
            worldPoint.z = rect.position.z;
            return true;
        }

        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint)
        {
            Vector3 w;
            ScreenPointToWorldPointInRectangle(rect, screenPoint, cam, out w);
            Vector3 l = rect.InverseTransformPoint(w);
            localPoint = new Vector2(l.x, l.y);
            return true;
        }

        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint) { return RectangleContainsScreenPoint(rect, screenPoint, null); }
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam)
        {
            Vector2 l;
            ScreenPointToLocalPointInRectangle(rect, screenPoint, cam, out l);
            return rect.rect.Contains(l);
        }

        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint)
        {
            if (cam == null) return new Vector2(worldPoint.x, worldPoint.y);
            Vector3 s = cam.WorldToScreenPoint(worldPoint);
            return new Vector2(s.x, s.y);
        }

        public static Vector2 PixelAdjustPoint(Vector2 point, Transform elementTransform, Canvas canvas) { return point; }
    }

    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }

    public class Canvas : Behaviour
    {
        static readonly List<Canvas> all = new List<Canvas>();

        public RenderMode renderMode = RenderMode.ScreenSpaceOverlay;
        public Camera worldCamera;
        public float planeDistance = 100f;
        public int sortingOrder;
        public int sortingLayerID;
        public string sortingLayerName = "Default";
        public bool overrideSorting;
        public bool pixelPerfect;
        public float referencePixelsPerUnit = 100f;
        public int targetDisplay;
        float m_ScaleFactor = 1f;

        public float scaleFactor { get { return m_ScaleFactor; } set { m_ScaleFactor = Mathf.Max(0.0001f, value); } }

        public override void OnAttach() { all.Add(this); }

        public bool isRootCanvas
        {
            get
            {
                Transform p = transform.parent;
                return p == null || p.GetComponentInParent<Canvas>() == null;
            }
        }

        public Canvas rootCanvas
        {
            get
            {
                Canvas c = this;
                Transform p = transform.parent;
                while (p != null)
                {
                    Canvas above = p.GetComponent<Canvas>();
                    if (above != null) c = above;
                    p = p.parent;
                }
                return c;
            }
        }

        public Rect pixelRect { get { return new Rect(0f, 0f, Screen.width, Screen.height); } }
        public float renderOrder { get { return sortingOrder; } }

        public static void ForceUpdateCanvases() { UpdateCanvases(); }

        /// <summary>The engine calls this each frame: every root canvas is sized from the screen.</summary>
        public static void UpdateCanvases()
        {
            foreach (Canvas c in all.ToArray())
            {
                if (c.destroyed) { all.Remove(c); continue; }
                if (c.isRootCanvas) c.DriveRoot();
            }
        }

        void DriveRoot()
        {
            RectTransform rt = transform as RectTransform;
            if (rt == null || renderMode == RenderMode.WorldSpace) return;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler != null) scaleFactor = scaler.ComputeScaleFactor();
            Vector2 size = new Vector2(Screen.width / scaleFactor, Screen.height / scaleFactor);
            Camera cam = worldCamera != null ? worldCamera : null;
            if (renderMode == RenderMode.ScreenSpaceCamera && cam != null)
            {
                // the canvas fills the camera's view at planeDistance: size.y canvas units span the view's height
                float viewHeight = cam.orthographic ? cam.orthographicSize * 2f : 2f * planeDistance * Mathf.Tan(30f * Mathf.Deg2Rad);
                Vector3 p = cam.transform.position + cam.transform.forward * planeDistance;
                rt.DriveRoot(size, p, cam.transform.rotation, viewHeight / size.y);
            }
            else
            {
                rt.DriveRoot(size, new Vector3(Screen.width / 2f, Screen.height / 2f, 0f), Quaternion.identity, scaleFactor);
            }
        }
    }

    public class CanvasGroup : Behaviour
    {
        public float alpha = 1f;
        public bool interactable = true;
        public bool blocksRaycasts = true;
        public bool ignoreParentGroups;
    }

    public class CanvasRenderer : Component
    {
        public bool cullTransparentMesh { get; set; }
        public void SetAlpha(float alpha) { }
        public void SetColor(Color color) { }
        public Color GetColor() { return Color.white; }
    }
}

namespace UnityEngine.EventSystems
{
    public abstract class UIBehaviour : MonoBehaviour
    {
        public virtual bool IsActive() { return isActiveAndEnabled; }
        public bool IsDestroyed() { return destroyed; }
    }

    public interface IEventSystemHandler { }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData eventData); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData eventData); }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData eventData); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData eventData); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData eventData); }
    public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData eventData); }
    public interface IDropHandler : IEventSystemHandler { void OnDrop(PointerEventData eventData); }
    public interface IScrollHandler : IEventSystemHandler { void OnScroll(PointerEventData eventData); }
    public interface ISelectHandler : IEventSystemHandler { void OnSelect(BaseEventData eventData); }
    public interface IDeselectHandler : IEventSystemHandler { void OnDeselect(BaseEventData eventData); }
    public interface IUpdateSelectedHandler : IEventSystemHandler { void OnUpdateSelected(BaseEventData eventData); }
    public interface IMoveHandler : IEventSystemHandler { void OnMove(AxisEventData eventData); }
    public interface ISubmitHandler : IEventSystemHandler { void OnSubmit(BaseEventData eventData); }
    public interface ICancelHandler : IEventSystemHandler { void OnCancel(BaseEventData eventData); }

    public enum MoveDirection { Left = 0, Up = 1, Right = 2, Down = 3, None = 4 }

    public class BaseEventData
    {
        public EventSystem currentInputModule { get { return EventSystem.current; } }
        public GameObject selectedObject { get { return EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject; } }
        public bool used { get; private set; }
        public void Use() { used = true; }
        public void Reset() { used = false; }
        public BaseEventData(EventSystem eventSystem) { }
    }

    public class AxisEventData : BaseEventData
    {
        public Vector2 moveVector { get; set; }
        public MoveDirection moveDir { get; set; }
        public AxisEventData(EventSystem eventSystem) : base(eventSystem) { }
    }

    public struct RaycastResult
    {
        public GameObject gameObject;
        public float distance;
        public int depth;
        public int sortingLayer;
        public int sortingOrder;
        public Vector3 worldPosition;
        public Vector3 worldNormal;
        public Vector2 screenPosition;
        public bool isValid { get { return gameObject != null; } }
        public void Clear() { gameObject = null; }
    }

    public class PointerEventData : BaseEventData
    {
        public enum InputButton { Left = 0, Right = 1, Middle = 2 }
        public enum FramePressState { Pressed, Released, PressedAndReleased, NotChanged }

        public GameObject pointerEnter { get; set; }
        public GameObject pointerPress { get; set; }
        public GameObject rawPointerPress { get; set; }
        public GameObject pointerDrag { get; set; }
        public GameObject lastPress { get; private set; }
        public RaycastResult pointerCurrentRaycast { get; set; }
        public RaycastResult pointerPressRaycast { get; set; }
        public List<GameObject> hovered = new List<GameObject>();
        public bool eligibleForClick { get; set; }
        public int pointerId { get; set; }
        public Vector2 position { get; set; }
        public Vector2 delta { get; set; }
        public Vector2 pressPosition { get; set; }
        public float clickTime { get; set; }
        public int clickCount { get; set; }
        public Vector2 scrollDelta { get; set; }
        public bool useDragThreshold { get; set; }
        public bool dragging { get; set; }
        public InputButton button { get; set; }
        public Camera enterEventCamera { get { return null; } }
        public Camera pressEventCamera { get { return null; } }
        public bool IsPointerMoving() { return delta.sqrMagnitude > 0f; }
        public bool IsScrolling() { return scrollDelta.sqrMagnitude > 0f; }
        public PointerEventData(EventSystem eventSystem) : base(eventSystem) { }
    }

    public delegate void EventFunction<T1>(T1 handler, BaseEventData eventData);

    public static class ExecuteEvents
    {
        public static EventFunction<IPointerEnterHandler> pointerEnterHandler { get { return (h, d) => h.OnPointerEnter((PointerEventData)d); } }
        public static EventFunction<IPointerExitHandler> pointerExitHandler { get { return (h, d) => h.OnPointerExit((PointerEventData)d); } }
        public static EventFunction<IPointerDownHandler> pointerDownHandler { get { return (h, d) => h.OnPointerDown((PointerEventData)d); } }
        public static EventFunction<IPointerUpHandler> pointerUpHandler { get { return (h, d) => h.OnPointerUp((PointerEventData)d); } }
        public static EventFunction<IPointerClickHandler> pointerClickHandler { get { return (h, d) => h.OnPointerClick((PointerEventData)d); } }
        public static EventFunction<ISelectHandler> selectHandler { get { return (h, d) => h.OnSelect(d); } }
        public static EventFunction<IDeselectHandler> deselectHandler { get { return (h, d) => h.OnDeselect(d); } }
        public static EventFunction<ISubmitHandler> submitHandler { get { return (h, d) => h.OnSubmit(d); } }
        public static EventFunction<ICancelHandler> cancelHandler { get { return (h, d) => h.OnCancel(d); } }
        public static EventFunction<IMoveHandler> moveHandler { get { return (h, d) => h.OnMove((AxisEventData)d); } }
        public static EventFunction<IScrollHandler> scrollHandler { get { return (h, d) => h.OnScroll((PointerEventData)d); } }

        /// <summary>Calls the handler on every component of the object that implements it; true if one did.</summary>
        public static bool Execute<T>(GameObject target, BaseEventData eventData, EventFunction<T> functor) where T : IEventSystemHandler
        {
            if (target == null) return false;
            bool any = false;
            foreach (Component c in target.components.ToArray())
            {
                if (!(c is T)) continue;
                Behaviour b = c as Behaviour;
                if (b != null && !b.isActiveAndEnabled) continue;
                functor((T)(object)c, eventData);
                any = true;
            }
            return any;
        }

        /// <summary>The first object from the target up that handles the event, after it has handled it.</summary>
        public static GameObject ExecuteHierarchy<T>(GameObject root, BaseEventData eventData, EventFunction<T> callbackFunction) where T : IEventSystemHandler
        {
            for (Transform t = root == null ? null : root.transform; t != null; t = t.parent)
                if (Execute(t.gameObject, eventData, callbackFunction)) return t.gameObject;
            return null;
        }

        public static bool CanHandleEvent<T>(GameObject go) where T : IEventSystemHandler
        {
            if (go == null) return false;
            foreach (Component c in go.components) if (c is T) return true;
            return false;
        }

        public static GameObject GetEventHandler<T>(GameObject root) where T : IEventSystemHandler
        {
            for (Transform t = root == null ? null : root.transform; t != null; t = t.parent)
                if (CanHandleEvent<T>(t.gameObject)) return t.gameObject;
            return null;
        }
    }

    public class EventSystem : UIBehaviour
    {
        public static EventSystem current { get; set; }

        GameObject selected;
        bool selecting;

        public GameObject firstSelectedGameObject;
        public bool sendNavigationEvents = true;
        public int pixelDragThreshold = 10;

        public GameObject currentSelectedGameObject { get { return selected; } }
        public bool alreadySelecting { get { return selecting; } }
        public bool isFocused { get { return true; } }

        public override void OnEnable()
        {
            if (current == null) current = this;
        }

        public override void OnDisable()
        {
            if (current == this) current = null;
        }

        public override void Start()
        {
            if (firstSelectedGameObject != null && selected == null) SetSelectedGameObject(firstSelectedGameObject);
        }

        public void SetSelectedGameObject(GameObject selected) { SetSelectedGameObject(selected, new BaseEventData(this)); }

        /// <summary>The old selection gets OnDeselect, the new OnSelect.</summary>
        public void SetSelectedGameObject(GameObject selected, BaseEventData pointer)
        {
            if (selecting)
            {
                Debug.LogError("Attempting to select " + (selected == null ? "null" : selected.name) + " while already selecting an object.");
                return;
            }
            selecting = true;
            if (selected != this.selected)
            {
                ExecuteEvents.Execute(this.selected, pointer, ExecuteEvents.deselectHandler);
                this.selected = selected;
                ExecuteEvents.Execute(selected, pointer, ExecuteEvents.selectHandler);
            }
            selecting = false;
        }

        /// <summary>There is no pointer raycasting here: false.</summary>
        public bool IsPointerOverGameObject() { return false; }
        public bool IsPointerOverGameObject(int pointerId) { return false; }
        public void RaycastAll(PointerEventData eventData, List<RaycastResult> raycastResults) { raycastResults.Clear(); }
        public void UpdateModules() { }
    }

    public abstract class BaseInputModule : UIBehaviour { }
    public abstract class BaseRaycaster : UIBehaviour { }
    public class PhysicsRaycaster : BaseRaycaster { }
    public class Physics2DRaycaster : PhysicsRaycaster { }
    public class StandaloneInputModule : BaseInputModule { }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule { }
}

namespace UnityEngine.UI
{
    public class GraphicRaycaster : BaseRaycaster
    {
        public enum BlockingObjects { None = 0, TwoD = 1, ThreeD = 2, All = 3 }
        public bool ignoreReversedGraphics = true;
        public BlockingObjects blockingObjects;
    }

    public abstract class Graphic : UIBehaviour
    {
        static Material s_Default;

        public Color color = Color.white;
        public bool raycastTarget = true;
        public Vector4 raycastPadding;
        Material m_Material;

        /// <summary>The colour a Selectable's tint (or CrossFadeColor) multiplies in: the canvas renderer's colour in Unity.</summary>
        public Color tint = Color.white;

        public static Material defaultGraphicMaterial { get { if (s_Default == null) s_Default = new Material(Shader.Find("UI/Default")); return s_Default; } }

        public virtual Material material { get { return m_Material != null ? m_Material : defaultGraphicMaterial; } set { m_Material = value; } }
        public virtual Material materialForRendering { get { return material; } }
        public virtual Material defaultMaterial { get { return defaultGraphicMaterial; } }
        public virtual Texture mainTexture { get { return null; } }
        public RectTransform rectTransform { get { return transform as RectTransform; } }
        public Canvas canvas { get { return GetComponentInParent<Canvas>(); } }
        public CanvasRenderer canvasRenderer { get { return GetComponent<CanvasRenderer>(); } }
        public int depth { get { return transform.GetSiblingIndex(); } }

        public virtual void SetAllDirty() { }
        public virtual void SetLayoutDirty() { }
        public virtual void SetVerticesDirty() { }
        public virtual void SetMaterialDirty() { }
        public virtual void Rebuild(CanvasUpdate update) { }
        public virtual void SetNativeSize() { }
        public void GraphicUpdateComplete() { }
        public void LayoutComplete() { }

        /// <summary>Tints the graphic (at once: there is no tween).</summary>
        public virtual void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha)
        {
            tint = useAlpha ? targetColor : new Color(targetColor.r, targetColor.g, targetColor.b, tint.a);
        }
        public virtual void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha, bool useRGB)
        {
            Color c = tint;
            if (useRGB) { c.r = targetColor.r; c.g = targetColor.g; c.b = targetColor.b; }
            if (useAlpha) c.a = targetColor.a;
            tint = c;
        }
        public virtual void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale) { tint = new Color(tint.r, tint.g, tint.b, alpha); }

        public virtual bool Raycast(Vector2 sp, Camera eventCamera)
        {
            RectTransform rt = rectTransform;
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, sp, eventCamera);
        }
    }

    public enum CanvasUpdate { Prelayout = 0, Layout = 1, PostLayout = 2, PreRender = 3, LatePreRender = 4, MaxUpdateValue = 5 }

    public abstract class MaskableGraphic : Graphic
    {
        public bool maskable = true;
        public bool isMaskingGraphic { get; set; }
        public UnityEvent<bool> onCullStateChanged = new UnityEvent<bool>();
    }

    public class Image : MaskableGraphic
    {
        public enum Type { Simple = 0, Sliced = 1, Tiled = 2, Filled = 3 }
        public enum FillMethod { Horizontal = 0, Vertical = 1, Radial90 = 2, Radial180 = 3, Radial360 = 4 }
        public enum OriginHorizontal { Left = 0, Right = 1 }
        public enum OriginVertical { Bottom = 0, Top = 1 }

        public Sprite sprite;
        public Sprite overrideSprite { get { return m_Override != null ? m_Override : sprite; } set { m_Override = value; } }
        Sprite m_Override;
        public Type type = Type.Simple;
        public bool preserveAspect;
        public bool fillCenter = true;
        public FillMethod fillMethod = FillMethod.Radial360;
        public float fillAmount { get { return m_FillAmount; } set { m_FillAmount = Mathf.Clamp01(value); } }
        float m_FillAmount = 1f;
        public bool fillClockwise = true;
        public int fillOrigin;
        public bool useSpriteMesh;
        public float pixelsPerUnitMultiplier = 1f;
        public float alphaHitTestMinimumThreshold;

        public bool hasBorder { get { Sprite s = overrideSprite; return s != null && s.border.sqrMagnitude > 0f; } }
        public float pixelsPerUnit { get { Sprite s = overrideSprite; return s == null ? 100f : s.pixelsPerUnit; } }
        public override Texture mainTexture { get { Sprite s = overrideSprite; return s == null ? null : s.texture; } }

        /// <summary>The rectangle takes the sprite's size in pixels (per canvas unit).</summary>
        public override void SetNativeSize()
        {
            Sprite s = overrideSprite;
            RectTransform rt = rectTransform;
            if (s == null || rt == null) return;
            rt.anchorMax = rt.anchorMin;
            rt.sizeDelta = new Vector2(s.rect.width / pixelsPerUnitMultiplier, s.rect.height / pixelsPerUnitMultiplier);
        }
    }

    public class RawImage : MaskableGraphic
    {
        public Texture texture;
        public Rect uvRect = new Rect(0f, 0f, 1f, 1f);
        public override Texture mainTexture { get { return texture; } }
    }

    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }
    public enum HorizontalWrapMode { Wrap = 0, Overflow = 1 }
    public enum VerticalWrapMode { Truncate = 0, Overflow = 1 }

    public class Font : Object
    {
        public int fontSize { get; set; }
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) { Font f = new Font(); f.name = fontname; f.fontSize = size; return f; }
    }

    public class Text : MaskableGraphic
    {
        public string text = "";
        public Font font;
        public int fontSize = 14;
        public FontStyle fontStyle;
        public TextAnchor alignment = TextAnchor.UpperLeft;
        public bool alignByGeometry;
        public float lineSpacing = 1f;
        public bool supportRichText = true;
        public bool resizeTextForBestFit;
        public int resizeTextMinSize = 10;
        public int resizeTextMaxSize = 40;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
        public float preferredWidth { get { return text.Length * fontSize * 0.5f; } }
        public float preferredHeight { get { return fontSize * lineSpacing; } }
    }

    public struct ColorBlock : IEquatable<ColorBlock>
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
        public Color selectedColor { get; set; }
        public Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }

        public static ColorBlock defaultColorBlock
        {
            get
            {
                ColorBlock c = new ColorBlock();
                c.normalColor = new Color(1f, 1f, 1f, 1f);
                c.highlightedColor = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f);
                c.pressedColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 1f);
                c.selectedColor = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f);
                c.disabledColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 0.5019608f);
                c.colorMultiplier = 1f;
                c.fadeDuration = 0.1f;
                return c;
            }
        }

        public bool Equals(ColorBlock o)
        {
            return normalColor == o.normalColor && highlightedColor == o.highlightedColor && pressedColor == o.pressedColor && selectedColor == o.selectedColor
                && disabledColor == o.disabledColor && colorMultiplier == o.colorMultiplier && fadeDuration == o.fadeDuration;
        }
        public override bool Equals(object obj) { return obj is ColorBlock && Equals((ColorBlock)obj); }
        public override int GetHashCode() { return normalColor.GetHashCode(); }
    }

    public struct SpriteState
    {
        public Sprite highlightedSprite { get; set; }
        public Sprite pressedSprite { get; set; }
        public Sprite selectedSprite { get; set; }
        public Sprite disabledSprite { get; set; }
    }

    public class AnimationTriggers
    {
        public string normalTrigger = "Normal";
        public string highlightedTrigger = "Highlighted";
        public string pressedTrigger = "Pressed";
        public string selectedTrigger = "Selected";
        public string disabledTrigger = "Disabled";
    }

    public struct Navigation
    {
        [Flags]
        public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }

        public Mode mode { get; set; }
        public bool wrapAround { get; set; }
        public Selectable selectOnUp { get; set; }
        public Selectable selectOnDown { get; set; }
        public Selectable selectOnLeft { get; set; }
        public Selectable selectOnRight { get; set; }

        public static Navigation defaultNavigation { get { Navigation n = new Navigation(); n.mode = Mode.Automatic; return n; } }
    }

    /// <summary>Something that can be selected and interacted with: its state (normal, highlighted, pressed, selected, disabled) tints its target
    /// graphic (ColorTint) or swaps its sprite (SpriteSwap).</summary>
    public class Selectable : UIBehaviour, IMoveHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public enum Transition { None = 0, ColorTint = 1, SpriteSwap = 2, Animation = 3 }
        protected enum SelectionState { Normal = 0, Highlighted = 1, Pressed = 2, Selected = 3, Disabled = 4 }

        protected static Selectable[] s_Selectables = new Selectable[10];
        protected static int s_SelectableCount = 0;

        public Navigation navigation = Navigation.defaultNavigation;
        public Transition transition = Transition.ColorTint;
        public ColorBlock colors = ColorBlock.defaultColorBlock;
        public SpriteState spriteState;
        public AnimationTriggers animationTriggers = new AnimationTriggers();
        public Graphic targetGraphic;
        bool m_Interactable = true;
        bool isPointerInside, isPointerDown, hasSelection;

        public static Selectable[] allSelectablesArray
        {
            get
            {
                Selectable[] a = new Selectable[s_SelectableCount];
                Array.Copy(s_Selectables, a, s_SelectableCount);
                return a;
            }
        }
        public static int allSelectableCount { get { return s_SelectableCount; } }
        public static int AllSelectablesNoAlloc(Selectable[] selectables)
        {
            int n = Math.Min(selectables.Length, s_SelectableCount);
            Array.Copy(s_Selectables, selectables, n);
            return n;
        }

        public Image image { get { return targetGraphic as Image; } set { targetGraphic = value; } }

        public bool interactable
        {
            get { return m_Interactable; }
            set
            {
                if (m_Interactable == value) return;
                m_Interactable = value;
                if (!value && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                    EventSystem.current.SetSelectedGameObject(null);
                DoStateTransition(currentSelectionState, true);
            }
        }

        /// <summary>Interactable, and so are the canvas groups above it.</summary>
        public virtual bool IsInteractable()
        {
            if (!m_Interactable) return false;
            for (Transform t = transform; t != null; t = t.parent)
            {
                CanvasGroup g = t.GetComponent<CanvasGroup>();
                if (g != null && g.enabled)
                {
                    if (!g.interactable) return false;
                    if (g.ignoreParentGroups) break;
                }
            }
            return true;
        }

        protected SelectionState currentSelectionState
        {
            get
            {
                if (!IsInteractable()) return SelectionState.Disabled;
                if (isPointerDown) return SelectionState.Pressed;
                if (hasSelection) return SelectionState.Selected;
                if (isPointerInside) return SelectionState.Highlighted;
                return SelectionState.Normal;
            }
        }

        public override void Awake()
        {
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
        }

        public override void OnEnable()
        {
            if (s_SelectableCount == s_Selectables.Length)
            {
                Selectable[] bigger = new Selectable[s_Selectables.Length * 2];
                Array.Copy(s_Selectables, bigger, s_SelectableCount);
                s_Selectables = bigger;
            }
            s_Selectables[s_SelectableCount++] = this;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) hasSelection = true;
            isPointerDown = false;
            DoStateTransition(currentSelectionState, true);
        }

        public override void OnDisable()
        {
            for (int i = 0; i < s_SelectableCount; i++)
            {
                if (s_Selectables[i] != this) continue;
                s_SelectableCount--;
                s_Selectables[i] = s_Selectables[s_SelectableCount];
                s_Selectables[s_SelectableCount] = null;
                break;
            }
            isPointerInside = false;
            isPointerDown = false;
            hasSelection = false;
            DoStateTransition(SelectionState.Normal, true);
        }

        protected bool IsHighlighted() { return IsActive() && IsInteractable() && isPointerInside && !isPointerDown && !hasSelection; }
        protected bool IsPressed() { return IsActive() && IsInteractable() && isPointerDown; }

        protected void UpdateSelectionState(BaseEventData eventData) { }

        protected virtual void DoStateTransition(SelectionState state, bool instant)
        {
            if (gameObject == null || !gameObject.activeInHierarchy) return;
            Color tintColor;
            Sprite transitionSprite;
            switch (state)
            {
                case SelectionState.Highlighted: tintColor = colors.highlightedColor; transitionSprite = spriteState.highlightedSprite; break;
                case SelectionState.Pressed: tintColor = colors.pressedColor; transitionSprite = spriteState.pressedSprite; break;
                case SelectionState.Selected: tintColor = colors.selectedColor; transitionSprite = spriteState.selectedSprite; break;
                case SelectionState.Disabled: tintColor = colors.disabledColor; transitionSprite = spriteState.disabledSprite; break;
                default: tintColor = colors.normalColor; transitionSprite = null; break;
            }
            if (transition == Transition.ColorTint && targetGraphic != null)
                targetGraphic.CrossFadeColor(tintColor * colors.colorMultiplier, instant ? 0f : colors.fadeDuration, true, true);
            else if (transition == Transition.SpriteSwap && image != null)
                image.overrideSprite = transitionSprite;
        }

        public virtual void Select()
        {
            if (EventSystem.current == null || EventSystem.current.alreadySelecting) return;
            EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (IsInteractable() && navigation.mode != Navigation.Mode.None && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject, eventData);
            isPointerDown = true;
            DoStateTransition(currentSelectionState, false);
        }

        public virtual void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            isPointerDown = false;
            DoStateTransition(currentSelectionState, false);
        }

        public virtual void OnPointerEnter(PointerEventData eventData) { isPointerInside = true; DoStateTransition(currentSelectionState, false); }
        public virtual void OnPointerExit(PointerEventData eventData) { isPointerInside = false; DoStateTransition(currentSelectionState, false); }
        public virtual void OnSelect(BaseEventData eventData) { hasSelection = true; DoStateTransition(currentSelectionState, false); }
        public virtual void OnDeselect(BaseEventData eventData) { hasSelection = false; DoStateTransition(currentSelectionState, false); }

        public virtual void OnMove(AxisEventData eventData)
        {
            Selectable next = null;
            switch (eventData.moveDir)
            {
                case MoveDirection.Right: next = FindSelectableOnRight(); break;
                case MoveDirection.Up: next = FindSelectableOnUp(); break;
                case MoveDirection.Left: next = FindSelectableOnLeft(); break;
                case MoveDirection.Down: next = FindSelectableOnDown(); break;
            }
            if (next != null && next.IsActive()) { eventData.Use(); next.Select(); }
        }

        public virtual Selectable FindSelectableOnLeft() { return Find(navigation.selectOnLeft, Vector3.left, Navigation.Mode.Horizontal); }
        public virtual Selectable FindSelectableOnRight() { return Find(navigation.selectOnRight, Vector3.right, Navigation.Mode.Horizontal); }
        public virtual Selectable FindSelectableOnUp() { return Find(navigation.selectOnUp, Vector3.up, Navigation.Mode.Vertical); }
        public virtual Selectable FindSelectableOnDown() { return Find(navigation.selectOnDown, Vector3.down, Navigation.Mode.Vertical); }

        Selectable Find(Selectable explicitOne, Vector3 dir, Navigation.Mode axis)
        {
            if (navigation.mode == Navigation.Mode.Explicit) return explicitOne;
            if ((navigation.mode & axis) != 0) return FindSelectable(transform.rotation * dir);
            return null;
        }

        /// <summary>The nearest selectable in that direction (by world position), as Unity's automatic navigation picks it.</summary>
        public Selectable FindSelectable(Vector3 dir)
        {
            dir = dir.normalized;
            Vector3 pos = transform.position;
            float best = float.NegativeInfinity;
            Selectable bestPick = null;
            for (int i = 0; i < s_SelectableCount; i++)
            {
                Selectable sel = s_Selectables[i];
                if (sel == this || !sel.IsInteractable() || sel.navigation.mode == Navigation.Mode.None) continue;
                Vector3 vec = sel.transform.position - pos;
                float dot = Vector3.Dot(dir, vec);
                if (dot <= 0f) continue;
                float score = dot / vec.sqrMagnitude;
                if (score > best) { best = score; bestPick = sel; }
            }
            return bestPick;
        }
    }

    public class Button : Selectable, IPointerClickHandler, ISubmitHandler
    {
        [Serializable]
        public class ButtonClickedEvent : UnityEvent { }

        public ButtonClickedEvent onClick = new ButtonClickedEvent();

        void Press()
        {
            if (!IsActive() || !IsInteractable()) return;
            onClick.Invoke();
        }

        public virtual void OnPointerClick(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) Press(); }
        public virtual void OnSubmit(BaseEventData eventData) { Press(); }
    }

    public class Toggle : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public enum ToggleTransition { None = 0, Fade = 1 }

        [Serializable]
        public class ToggleEvent : UnityEvent<bool> { }

        public ToggleTransition toggleTransition = ToggleTransition.Fade;
        public Graphic graphic;
        public ToggleEvent onValueChanged = new ToggleEvent();
        ToggleGroup m_Group;
        bool m_IsOn;

        public ToggleGroup group
        {
            get { return m_Group; }
            set
            {
                if (m_Group != null) m_Group.UnregisterToggle(this);
                m_Group = value;
                if (m_Group != null && IsActive()) m_Group.RegisterToggle(this);
                if (value != null && m_IsOn && IsActive()) value.NotifyToggleOn(this, true);
            }
        }

        public bool isOn { get { return m_IsOn; } set { Set(value, true); } }
        public void SetIsOnWithoutNotify(bool value) { Set(value, false); }

        public override void OnEnable()
        {
            base.OnEnable();
            if (m_Group != null) m_Group.RegisterToggle(this);
            PlayEffect(true);
        }

        public override void OnDisable()
        {
            if (m_Group != null) m_Group.UnregisterToggle(this);
            base.OnDisable();
        }

        void Set(bool value, bool sendCallback)
        {
            if (m_IsOn == value) return;
            m_IsOn = value;
            if (m_Group != null && m_Group.isActiveAndEnabled && IsActive())
            {
                if (m_IsOn || (!m_Group.AnyTogglesOn() && !m_Group.allowSwitchOff))
                {
                    m_IsOn = true;
                    m_Group.NotifyToggleOn(this, sendCallback);
                }
            }
            PlayEffect(toggleTransition == ToggleTransition.None);
            if (sendCallback) onValueChanged.Invoke(m_IsOn);
        }

        void PlayEffect(bool instant)
        {
            if (graphic == null) return;
            graphic.CrossFadeAlpha(m_IsOn ? 1f : 0f, instant ? 0f : 0.1f, true);
        }

        public virtual void OnPointerClick(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable()) isOn = !isOn; }
        public virtual void OnSubmit(BaseEventData eventData) { if (IsActive() && IsInteractable()) isOn = !isOn; }
    }

    public class ToggleGroup : UIBehaviour
    {
        public bool allowSwitchOff;
        readonly List<Toggle> toggles = new List<Toggle>();

        public void RegisterToggle(Toggle toggle) { if (!toggles.Contains(toggle)) toggles.Add(toggle); }
        public void UnregisterToggle(Toggle toggle) { toggles.Remove(toggle); }

        public void NotifyToggleOn(Toggle toggle, bool sendCallback = true)
        {
            foreach (Toggle t in toggles.ToArray())
            {
                if (t == toggle) continue;
                if (sendCallback) t.isOn = false;
                else t.SetIsOnWithoutNotify(false);
            }
        }

        public bool AnyTogglesOn()
        {
            foreach (Toggle t in toggles) if (t.isOn) return true;
            return false;
        }

        public IEnumerable<Toggle> ActiveToggles()
        {
            foreach (Toggle t in toggles.ToArray()) if (t.isOn) yield return t;
        }

        public Toggle GetFirstActiveToggle()
        {
            foreach (Toggle t in toggles) if (t.isOn) return t;
            return null;
        }

        public void SetAllTogglesOff(bool sendCallback = true)
        {
            bool old = allowSwitchOff;
            allowSwitchOff = true;
            foreach (Toggle t in toggles.ToArray())
            {
                if (sendCallback) t.isOn = false;
                else t.SetIsOnWithoutNotify(false);
            }
            allowSwitchOff = old;
        }

        public void EnsureValidState()
        {
            if (!allowSwitchOff && !AnyTogglesOn() && toggles.Count != 0) toggles[0].isOn = true;
        }
    }

    public class Slider : Selectable
    {
        public enum Direction { LeftToRight = 0, RightToLeft = 1, BottomToTop = 2, TopToBottom = 3 }

        [Serializable]
        public class SliderEvent : UnityEvent<float> { }

        public RectTransform fillRect;
        public RectTransform handleRect;
        public Direction direction;
        public float minValue;
        public float maxValue = 1f;
        public bool wholeNumbers;
        public SliderEvent onValueChanged = new SliderEvent();
        protected float m_Value;

        public virtual float value
        {
            get { return wholeNumbers ? Mathf.Round(m_Value) : m_Value; }
            set { Set(value, true); }
        }

        public float normalizedValue
        {
            get { return Mathf.Approximately(minValue, maxValue) ? 0f : Mathf.InverseLerp(minValue, maxValue, value); }
            set { this.value = Mathf.Lerp(minValue, maxValue, value); }
        }

        public virtual void SetValueWithoutNotify(float input) { Set(input, false); }

        float ClampValue(float input)
        {
            float v = Mathf.Clamp(input, minValue, maxValue);
            return wholeNumbers ? Mathf.Round(v) : v;
        }

        protected virtual void Set(float input, bool sendCallback)
        {
            float newValue = ClampValue(input);
            if (m_Value == newValue) return;
            m_Value = newValue;
            UpdateVisuals();
            if (sendCallback) onValueChanged.Invoke(newValue);
        }

        int Axis { get { return direction == Direction.LeftToRight || direction == Direction.RightToLeft ? 0 : 1; } }
        bool Reverse { get { return direction == Direction.RightToLeft || direction == Direction.TopToBottom; } }

        static Vector2 With(Vector2 v, int axis, float value) { if (axis == 0) v.x = value; else v.y = value; return v; }

        /// <summary>The fill stretches to the value and the handle sits at it (or a filled Image fills that much), as Unity's slider lays them out.</summary>
        void UpdateVisuals()
        {
            float nv = normalizedValue;
            if (fillRect != null)
            {
                Image fillImage = fillRect.GetComponent<Image>();
                Vector2 anchorMin = Vector2.zero, anchorMax = Vector2.one;
                if (fillImage != null && fillImage.type == Image.Type.Filled) fillImage.fillAmount = nv;
                else if (Reverse) anchorMin = With(anchorMin, Axis, 1f - nv);
                else anchorMax = With(anchorMax, Axis, nv);
                fillRect.anchorMin = anchorMin;
                fillRect.anchorMax = anchorMax;
            }
            if (handleRect != null)
            {
                Vector2 anchorMin = Vector2.zero, anchorMax = Vector2.one;
                float at = Reverse ? 1f - nv : nv;
                anchorMin = With(anchorMin, Axis, at);
                anchorMax = With(anchorMax, Axis, at);
                handleRect.anchorMin = anchorMin;
                handleRect.anchorMax = anchorMax;
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            Set(m_Value, false);
            UpdateVisuals();
        }
    }

    public class Scrollbar : Selectable
    {
        public enum Direction { LeftToRight = 0, RightToLeft = 1, BottomToTop = 2, TopToBottom = 3 }

        [Serializable]
        public class ScrollEvent : UnityEvent<float> { }

        public RectTransform handleRect;
        public Direction direction;
        public ScrollEvent onValueChanged = new ScrollEvent();
        float m_Value;
        float m_Size = 0.2f;
        int m_NumberOfSteps;

        public float value
        {
            get
            {
                float v = m_Value;
                if (m_NumberOfSteps > 1) v = Mathf.Round(v * (m_NumberOfSteps - 1)) / (m_NumberOfSteps - 1);
                return v;
            }
            set { Set(value, true); }
        }
        public float size { get { return m_Size; } set { m_Size = Mathf.Clamp01(value); UpdateVisuals(); } }
        public int numberOfSteps { get { return m_NumberOfSteps; } set { m_NumberOfSteps = value; Set(m_Value, true); } }

        public virtual void SetValueWithoutNotify(float input) { Set(input, false); }

        void Set(float input, bool sendCallback)
        {
            float current = m_Value;
            m_Value = Mathf.Clamp01(input);
            if (current == value) return;
            UpdateVisuals();
            if (sendCallback) onValueChanged.Invoke(value);
        }

        void UpdateVisuals()
        {
            if (handleRect == null) return;
            int axis = direction == Direction.LeftToRight || direction == Direction.RightToLeft ? 0 : 1;
            bool reverse = direction == Direction.RightToLeft || direction == Direction.TopToBottom;
            float movement = Mathf.Clamp01(value) * (1f - size);
            float lo = reverse ? 1f - movement - size : movement;
            Vector2 anchorMin = Vector2.zero, anchorMax = Vector2.one;
            if (axis == 0) { anchorMin.x = lo; anchorMax.x = lo + size; }
            else { anchorMin.y = lo; anchorMax.y = lo + size; }
            handleRect.anchorMin = anchorMin;
            handleRect.anchorMax = anchorMax;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            UpdateVisuals();
        }
    }

    public class ScrollRect : UIBehaviour, IScrollHandler
    {
        public enum MovementType { Unrestricted = 0, Elastic = 1, Clamped = 2 }
        public enum ScrollbarVisibility { Permanent = 0, AutoHide = 1, AutoHideAndExpandViewport = 2 }

        [Serializable]
        public class ScrollRectEvent : UnityEvent<Vector2> { }

        public RectTransform content;
        public RectTransform viewport;
        public bool horizontal = true;
        public bool vertical = true;
        public MovementType movementType = MovementType.Elastic;
        public float elasticity = 0.1f;
        public bool inertia = true;
        public float decelerationRate = 0.135f;
        public float scrollSensitivity = 1f;
        public ScrollbarVisibility horizontalScrollbarVisibility;
        public ScrollbarVisibility verticalScrollbarVisibility;
        public ScrollRectEvent onValueChanged = new ScrollRectEvent();
        public Vector2 velocity;
        Scrollbar m_Horizontal, m_Vertical;

        public Scrollbar horizontalScrollbar
        {
            get { return m_Horizontal; }
            set { if (m_Horizontal != null) m_Horizontal.onValueChanged.RemoveListener(SetHorizontal); m_Horizontal = value; if (value != null) value.onValueChanged.AddListener(SetHorizontal); }
        }
        public Scrollbar verticalScrollbar
        {
            get { return m_Vertical; }
            set { if (m_Vertical != null) m_Vertical.onValueChanged.RemoveListener(SetVertical); m_Vertical = value; if (value != null) value.onValueChanged.AddListener(SetVertical); }
        }

        RectTransform View { get { return viewport != null ? viewport : transform as RectTransform; } }

        /// <summary>The content's and the view's extent along an axis, in the view's space.</summary>
        void Extents(int axis, out float contentMin, out float contentSize, out float viewMin, out float viewSize)
        {
            Vector3[] c = new Vector3[4];
            content.GetWorldCorners(c);
            RectTransform view = View;
            Vector3 a = view.InverseTransformPoint(c[0]), b = view.InverseTransformPoint(c[2]);
            Rect vr = view.rect;
            contentMin = axis == 0 ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y);
            contentSize = axis == 0 ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.y - a.y);
            viewMin = axis == 0 ? vr.xMin : vr.yMin;
            viewSize = axis == 0 ? vr.width : vr.height;
        }

        float GetNormalized(int axis)
        {
            if (content == null) return 0f;
            float cMin, cSize, vMin, vSize;
            Extents(axis, out cMin, out cSize, out vMin, out vSize);
            if (cSize <= vSize) return vMin > cMin ? 1f : 0f;
            return (vMin - cMin) / (cSize - vSize);
        }

        /// <summary>Moves the content so the view shows that part of it (0 the start, 1 the end), as Unity's ScrollRect does.</summary>
        void SetNormalized(float value, int axis)
        {
            if (content == null) return;
            float cMin, cSize, vMin, vSize;
            Extents(axis, out cMin, out cSize, out vMin, out vSize);
            float hidden = cSize - vSize;
            float newMin = vMin - value * hidden;
            float offset = newMin - cMin;
            if (Mathf.Abs(offset) <= 0.01f) return;
            Vector2 p = content.anchoredPosition;
            if (axis == 0) p.x += offset; else p.y += offset;
            content.anchoredPosition = p;
            velocity = Vector2.zero;
            onValueChanged.Invoke(normalizedPosition);
        }

        void SetHorizontal(float value) { SetNormalized(value, 0); }
        void SetVertical(float value) { SetNormalized(value, 1); }

        public Vector2 normalizedPosition
        {
            get { return new Vector2(horizontalNormalizedPosition, verticalNormalizedPosition); }
            set { SetNormalized(value.x, 0); SetNormalized(value.y, 1); }
        }
        public float horizontalNormalizedPosition { get { return GetNormalized(0); } set { SetNormalized(value, 0); } }
        public float verticalNormalizedPosition { get { return GetNormalized(1); } set { SetNormalized(value, 1); } }

        public void StopMovement() { velocity = Vector2.zero; }

        public virtual void OnScroll(PointerEventData data)
        {
            if (!IsActive() || content == null) return;
            Vector2 p = content.anchoredPosition;
            if (vertical) p.y -= data.scrollDelta.y * scrollSensitivity;
            if (horizontal) p.x += data.scrollDelta.x * scrollSensitivity;
            content.anchoredPosition = p;
            onValueChanged.Invoke(normalizedPosition);
        }
    }

    public class InputField : Selectable
    {
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }

        [Serializable]
        public class SubmitEvent : UnityEvent<string> { }
        [Serializable]
        public class OnChangeEvent : UnityEvent<string> { }

        string m_Text = "";

        public Text textComponent;
        public Graphic placeholder;
        public ContentType contentType;
        public LineType lineType;
        public int characterLimit;
        public bool readOnly;
        public OnChangeEvent onValueChanged = new OnChangeEvent();
        public SubmitEvent onEndEdit = new SubmitEvent();
        public SubmitEvent onSubmit = new SubmitEvent();

        public string text
        {
            get { return m_Text; }
            set
            {
                string v = value ?? "";
                if (characterLimit > 0 && v.Length > characterLimit) v = v.Substring(0, characterLimit);
                if (v == m_Text) return;
                m_Text = v;
                if (textComponent != null) textComponent.text = v;
                onValueChanged.Invoke(v);
            }
        }

        public void SetTextWithoutNotify(string input) { m_Text = input ?? ""; if (textComponent != null) textComponent.text = m_Text; }
        public bool isFocused { get; private set; }
        public void ActivateInputField() { isFocused = true; }
        public void DeactivateInputField() { isFocused = false; onEndEdit.Invoke(m_Text); }
    }

    public static class LayoutRebuilder
    {
        public static void ForceRebuildLayoutImmediate(RectTransform layoutRoot) { }
        public static void MarkLayoutForRebuild(RectTransform rect) { }
    }

    public abstract class LayoutGroup : UIBehaviour
    {
        public RectOffset padding = new RectOffset();
        public TextAnchor childAlignment;
    }

    public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing;
        public bool childForceExpandWidth = true;
        public bool childForceExpandHeight = true;
        public bool childControlWidth;
        public bool childControlHeight;
        public bool childScaleWidth;
        public bool childScaleHeight;
        public bool reverseArrangement;
    }

    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class GridLayoutGroup : LayoutGroup
    {
        public enum Corner { UpperLeft = 0, UpperRight = 1, LowerLeft = 2, LowerRight = 3 }
        public enum Axis { Horizontal = 0, Vertical = 1 }
        public enum Constraint { Flexible = 0, FixedColumnCount = 1, FixedRowCount = 2 }
        public Corner startCorner;
        public Axis startAxis;
        public Vector2 cellSize = new Vector2(100f, 100f);
        public Vector2 spacing;
        public Constraint constraint;
        public int constraintCount = 2;
    }

    public class ContentSizeFitter : UIBehaviour
    {
        public enum FitMode { Unconstrained = 0, MinSize = 1, PreferredSize = 2 }
        public FitMode horizontalFit;
        public FitMode verticalFit;
    }

    public class LayoutElement : UIBehaviour
    {
        public bool ignoreLayout;
        public float minWidth = -1f, minHeight = -1f, preferredWidth = -1f, preferredHeight = -1f, flexibleWidth = -1f, flexibleHeight = -1f;
        public int layoutPriority = 1;
    }

    public class AspectRatioFitter : UIBehaviour
    {
        public enum AspectMode { None = 0, WidthControlsHeight = 1, HeightControlsWidth = 2, FitInParent = 3, EnvelopeParent = 4 }
        public AspectMode aspectMode;
        public float aspectRatio = 1f;
    }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize = 0, ScaleWithScreenSize = 1, ConstantPhysicalSize = 2 }
        public enum ScreenMatchMode { MatchWidthOrHeight = 0, Expand = 1, Shrink = 2 }
        public enum Unit { Centimeters = 0, Millimeters = 1, Inches = 2, Points = 3, Picas = 4 }

        public ScaleMode uiScaleMode;
        public float scaleFactor = 1f;
        public float referencePixelsPerUnit = 100f;
        public Vector2 referenceResolution = new Vector2(800f, 600f);
        public ScreenMatchMode screenMatchMode;
        public float matchWidthOrHeight;
        public Unit physicalUnit = Unit.Points;
        public float fallbackScreenDPI = 96f;
        public float defaultSpriteDPI = 96f;

        /// <summary>The canvas's scale factor for the screen, as Unity's CanvasScaler computes it.</summary>
        public float ComputeScaleFactor()
        {
            switch (uiScaleMode)
            {
                case ScaleMode.ScaleWithScreenSize:
                {
                    float w = Screen.width / referenceResolution.x, h = Screen.height / referenceResolution.y;
                    if (screenMatchMode == ScreenMatchMode.Expand) return Mathf.Min(w, h);
                    if (screenMatchMode == ScreenMatchMode.Shrink) return Mathf.Max(w, h);
                    float logW = Mathf.Log(w, 2f), logH = Mathf.Log(h, 2f);
                    return Mathf.Pow(2f, Mathf.Lerp(logW, logH, matchWidthOrHeight));
                }
                case ScaleMode.ConstantPhysicalSize:
                {
                    float dpi = Screen.dpi > 0f ? Screen.dpi : fallbackScreenDPI;
                    float target = physicalUnit == Unit.Centimeters ? 2.54f : physicalUnit == Unit.Millimeters ? 25.4f : physicalUnit == Unit.Inches ? 1f
                        : physicalUnit == Unit.Points ? 72f : 6f;
                    return dpi / target * referencePixelsPerUnit / 100f;
                }
                default:
                    return scaleFactor;
            }
        }
    }

    public class Mask : UIBehaviour { public bool showMaskGraphic = true; }
    public class RectMask2D : UIBehaviour { public Vector4 padding; public Vector2Int softness; }

    public abstract class BaseMeshEffect : UIBehaviour { }
    public class Shadow : BaseMeshEffect
    {
        public Color effectColor = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 effectDistance = new Vector2(1f, -1f);
        public bool useGraphicAlpha = true;
    }
    public class Outline : Shadow { }
}

namespace UnityEngine
{
    public enum TextAnchor { UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5, LowerLeft = 6, LowerCenter = 7, LowerRight = 8 }

    public class RectOffset
    {
        public int left, right, top, bottom;
        public RectOffset() { }
        public RectOffset(int left, int right, int top, int bottom) { this.left = left; this.right = right; this.top = top; this.bottom = bottom; }
        public int horizontal { get { return left + right; } }
        public int vertical { get { return top + bottom; } }
        public Rect Add(Rect rect) { return new Rect(rect.x - left, rect.y - bottom, rect.width + horizontal, rect.height + vertical); }
        public Rect Remove(Rect rect) { return new Rect(rect.x + left, rect.y + bottom, rect.width - horizontal, rect.height - vertical); }
    }
}
