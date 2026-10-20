// CCSharp --unity-engine --dna: the rest of UnityEngine, in full C#.  These files are added only with --dna: a class here that Crust cannot lower
// runs on DotNetAnywhere, so they are written as Unity's API is, not to the subset.
//
// Unity's attributes.  The engine has no inspector and no serializer, so they mark and do nothing; their constructors take what Unity's take.

using System;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeReference : Attribute { }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)] public sealed class HideInInspector : Attribute { }
    // (each attribute below derives from Attribute directly, not from PropertyAttribute as in Unity: attribute classes are lowered on both sides, and
    // Crust lays a base out before the classes that derive from it, which the emitter does not order)
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public class PropertyAttribute : Attribute { public int order { get; set; } }
    public sealed class HeaderAttribute : Attribute { public readonly string header; public HeaderAttribute(string header) { this.header = header; } }
    public sealed class TooltipAttribute : Attribute { public readonly string tooltip; public TooltipAttribute(string tooltip) { this.tooltip = tooltip; } }
    public sealed class SpaceAttribute : Attribute
    {
        public readonly float height;
        public SpaceAttribute() { height = 8f; }
        public SpaceAttribute(float height) { this.height = height; }
    }
    public sealed class RangeAttribute : Attribute
    {
        public readonly float min, max;
        public RangeAttribute(float min, float max) { this.min = min; this.max = max; }
    }
    public sealed class MinAttribute : Attribute { public readonly float min; public MinAttribute(float min) { this.min = min; } }
    public sealed class MultilineAttribute : Attribute
    {
        public readonly int lines;
        public MultilineAttribute() { lines = 3; }
        public MultilineAttribute(int lines) { this.lines = lines; }
    }
    public sealed class TextAreaAttribute : Attribute
    {
        public readonly int minLines, maxLines;
        public TextAreaAttribute() { minLines = 3; maxLines = 3; }
        public TextAreaAttribute(int minLines, int maxLines) { this.minLines = minLines; this.maxLines = maxLines; }
    }
    public sealed class ColorUsageAttribute : Attribute
    {
        public readonly bool showAlpha, hdr;
        public ColorUsageAttribute(bool showAlpha) { this.showAlpha = showAlpha; }
        public ColorUsageAttribute(bool showAlpha, bool hdr) { this.showAlpha = showAlpha; this.hdr = hdr; }
    }
    public sealed class DelayedAttribute : Attribute { }
    public sealed class ContextMenuItemAttribute : Attribute
    {
        public readonly string name, function;
        public ContextMenuItemAttribute(string name, string function) { this.name = name; this.function = function; }
    }

    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteInEditMode : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteAlways : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class SelectionBaseAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class HelpURLAttribute : Attribute { public HelpURLAttribute(string url) { } }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute { public int order { get; private set; } public DefaultExecutionOrder(int order) { this.order = order; } }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        public Type m_Type0, m_Type1, m_Type2;
        public RequireComponent(Type requiredComponent) { m_Type0 = requiredComponent; }
        public RequireComponent(Type requiredComponent, Type requiredComponent2) { m_Type0 = requiredComponent; m_Type1 = requiredComponent2; }
        public RequireComponent(Type requiredComponent, Type requiredComponent2, Type requiredComponent3)
        {
            m_Type0 = requiredComponent; m_Type1 = requiredComponent2; m_Type2 = requiredComponent3;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenu : Attribute
    {
        public string componentMenu { get; private set; }
        public int componentOrder { get; private set; }
        public AddComponentMenu(string menuName) { componentMenu = menuName; }
        public AddComponentMenu(string menuName, int order) { componentMenu = menuName; componentOrder = order; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ContextMenu : Attribute
    {
        public readonly string menuItem;
        public ContextMenu(string itemName) { menuItem = itemName; }
        public ContextMenu(string itemName, bool isValidateFunction) { menuItem = itemName; }
        public ContextMenu(string itemName, bool isValidateFunction, int priority) { menuItem = itemName; }
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeLoadType loadType { get; private set; }
        public RuntimeInitializeOnLoadMethodAttribute() { loadType = RuntimeInitializeLoadType.AfterSceneLoad; }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { this.loadType = loadType; }
    }
}

namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class FormerlySerializedAsAttribute : Attribute
    {
        public string oldName { get; private set; }
        public FormerlySerializedAsAttribute(string oldName) { this.oldName = oldName; }
    }
}
