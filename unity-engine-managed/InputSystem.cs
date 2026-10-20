// UnityEngine.InputSystem (the Input System package), over the state the host reports (unity-engine/Input.cs): a Keyboard and a Mouse once the host
// has reported them, a Gamepad for each pad it reports (GLFW's standard layout), no Touchscreen and no Joystick on a desktop.  A control reads the
// state of this frame, and wasPressedThisFrame compares it with the last one.  InputSystem.onEvent is called once a frame per device whose state
// changed, before scripts run, and -- as in Unity, where an event comes before its state is applied -- controls read the previous frame's values
// while it runs (ReadValueFromEvent gives the new ones).
//
// Input actions are declared but never fire: their bindings are serialized data the packer does not write.  The old Input class is here too
// (Input.GetKey(KeyCode.A) ...), with GetAxis unsmoothed.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace UnityEngine.InputSystem.Utilities
{
    public struct ReadOnlyArray<TValue> : IEnumerable<TValue>
    {
        readonly TValue[] items;

        public ReadOnlyArray(TValue[] array) { items = array; }

        public int Count { get { return items == null ? 0 : items.Length; } }
        public TValue this[int index] { get { return items[index]; } }

        public TValue[] ToArray() { return items == null ? new TValue[0] : (TValue[])items.Clone(); }
        public int IndexOf(TValue value)
        {
            for (int i = 0; i < Count; i++) if (EqualityComparer<TValue>.Default.Equals(items[i], value)) return i;
            return -1;
        }
        public bool Contains(TValue value) { return IndexOf(value) >= 0; }

        public IEnumerator<TValue> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return items[i];
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}

namespace UnityEngine.InputSystem
{
    public enum Key
    {
        None = 0,
        Space = 1,
        Enter = 2,
        Tab = 3,
        Backquote = 4,
        Quote = 5,
        Semicolon = 6,
        Comma = 7,
        Period = 8,
        Slash = 9,
        Backslash = 10,
        LeftBracket = 11,
        RightBracket = 12,
        Minus = 13,
        Equals = 14,
        A = 15,
        B = 16,
        C = 17,
        D = 18,
        E = 19,
        F = 20,
        G = 21,
        H = 22,
        I = 23,
        J = 24,
        K = 25,
        L = 26,
        M = 27,
        N = 28,
        O = 29,
        P = 30,
        Q = 31,
        R = 32,
        S = 33,
        T = 34,
        U = 35,
        V = 36,
        W = 37,
        X = 38,
        Y = 39,
        Z = 40,
        Digit1 = 41,
        Digit2 = 42,
        Digit3 = 43,
        Digit4 = 44,
        Digit5 = 45,
        Digit6 = 46,
        Digit7 = 47,
        Digit8 = 48,
        Digit9 = 49,
        Digit0 = 50,
        LeftShift = 51,
        RightShift = 52,
        LeftAlt = 53,
        RightAlt = 54,
        LeftCtrl = 55,
        RightCtrl = 56,
        LeftMeta = 57,
        RightMeta = 58,
        ContextMenu = 59,
        Escape = 60,
        LeftArrow = 61,
        RightArrow = 62,
        UpArrow = 63,
        DownArrow = 64,
        Backspace = 65,
        PageDown = 66,
        PageUp = 67,
        Home = 68,
        End = 69,
        Insert = 70,
        Delete = 71,
        CapsLock = 72,
        NumLock = 73,
        PrintScreen = 74,
        ScrollLock = 75,
        Pause = 76,
        NumpadEnter = 77,
        NumpadDivide = 78,
        NumpadMultiply = 79,
        NumpadPlus = 80,
        NumpadMinus = 81,
        NumpadPeriod = 82,
        NumpadEquals = 83,
        Numpad0 = 84,
        Numpad1 = 85,
        Numpad2 = 86,
        Numpad3 = 87,
        Numpad4 = 88,
        Numpad5 = 89,
        Numpad6 = 90,
        Numpad7 = 91,
        Numpad8 = 92,
        Numpad9 = 93,
        F1 = 94,
        F2 = 95,
        F3 = 96,
        F4 = 97,
        F5 = 98,
        F6 = 99,
        F7 = 100,
        F8 = 101,
        F9 = 102,
        F10 = 103,
        F11 = 104,
        F12 = 105,
        OEM1 = 106,
        OEM2 = 107,
        OEM3 = 108,
        OEM4 = 109,
        OEM5 = 110,
        IMESelected = 111,
        AltGr = RightAlt, LeftWindows = LeftMeta, LeftApple = LeftMeta, LeftCommand = LeftMeta,
        RightWindows = RightMeta, RightApple = RightMeta, RightCommand = RightMeta
    }

    public abstract class InputControl
    {
        internal readonly List<InputControl> kids = new List<InputControl>();

        public string name { get; internal set; }
        public string displayName { get; internal set; }
        public string shortDisplayName { get { return displayName; } }
        public InputDevice device { get; internal set; }
        public InputControl parent { get; internal set; }
        public bool noisy { get { return false; } }
        public bool synthetic { get; internal set; }

        public string path { get { return parent == null ? "/" + name : parent.path + "/" + name; } }
        public ReadOnlyArray<InputControl> children { get { return new ReadOnlyArray<InputControl>(kids.ToArray()); } }

        public float magnitude { get { return EvaluateMagnitude(); } }
        public virtual float EvaluateMagnitude() { return -1f; }
        public bool IsActuated(float threshold = 0f)
        {
            float m = EvaluateMagnitude();
            return threshold > 0f ? m >= threshold : m > 0f;
        }
        public bool IsPressed(float buttonPressPoint = 0f) { return EvaluateMagnitude() >= (buttonPressPoint > 0f ? buttonPressPoint : ButtonControl.s_GlobalDefaultButtonPressPoint); }

        public abstract object ReadValueAsObject();
        internal abstract bool ChangedThisFrame();

        public InputControl this[string path]
        {
            get
            {
                InputControl c = TryGetChildControl(path);
                if (c == null) throw new KeyNotFoundException("Cannot find control '" + path + "' as child of '" + this.path + "'");
                return c;
            }
        }

        /// <summary>The child of that name (names compare without case, as Unity's do), or a descendant by path ("stick/x").</summary>
        public InputControl TryGetChildControl(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.IndexOf('/');
            string first = slash < 0 ? path : path.Substring(0, slash);
            foreach (InputControl c in kids)
            {
                if (string.Compare(c.name.ToLowerInvariant(), first.ToLowerInvariant()) != 0) continue;
                return slash < 0 ? c : c.TryGetChildControl(path.Substring(slash + 1));
            }
            return null;
        }

        public TControl TryGetChildControl<TControl>(string path) where TControl : InputControl { return TryGetChildControl(path) as TControl; }

        internal TControl Add<TControl>(TControl c, string controlName, string display) where TControl : InputControl
        {
            c.name = controlName;
            c.displayName = display ?? controlName;
            c.parent = this;
            InputDevice d = this as InputDevice;
            c.device = d ?? device;
            kids.Add(c);
            c.device.controlList.Add(c);
            return c;
        }

        public override string ToString() { return GetType().Name + ":" + path; }
    }

    public abstract class InputControl<TValue> : InputControl where TValue : struct
    {
        public TValue value { get { return ReadValue(); } }

        /// <summary>This frame's value (the last frame's while InputSystem.onEvent runs: an event comes before its state is applied).</summary>
        public TValue ReadValue() { return InputSystem.dispatching ? ReadBefore() : ReadNow(); }
        public TValue ReadValueFromPreviousFrame() { return ReadBefore(); }
        public TValue ReadUnprocessedValue() { return ReadValue(); }
        public TValue ReadValueFromEvent(InputEventPtr inputEvent) { return ReadNow(); }
        public TValue ReadDefaultValue() { return default(TValue); }

        internal abstract TValue ReadNow();
        internal abstract TValue ReadBefore();
        internal override bool ChangedThisFrame() { return !EqualityComparer<TValue>.Default.Equals(ReadNow(), ReadBefore()); }

        public override object ReadValueAsObject() { return ReadValue(); }
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class AxisControl : InputControl<float>
    {
        internal Func<float> now = () => 0f;
        internal Func<float> before = () => 0f;

        internal override float ReadNow() { return now(); }
        internal override float ReadBefore() { return before(); }
        public override float EvaluateMagnitude() { return Mathf.Abs(ReadValue()); }
    }

    public class ButtonControl : AxisControl
    {
        public static float s_GlobalDefaultButtonPressPoint = 0.5f;
        public float pressPoint = -1f;

        public float pressPointOrDefault { get { return pressPoint > 0f ? pressPoint : s_GlobalDefaultButtonPressPoint; } }
        public bool IsValueConsideredPressed(float value) { return value >= pressPointOrDefault; }
        public bool isPressed { get { return IsValueConsideredPressed(ReadValue()); } }
        public bool wasPressedThisFrame { get { return IsValueConsideredPressed(ReadNow()) && !IsValueConsideredPressed(ReadBefore()); } }
        public bool wasReleasedThisFrame { get { return !IsValueConsideredPressed(ReadNow()) && IsValueConsideredPressed(ReadBefore()); } }
    }

    public class KeyControl : ButtonControl
    {
        public Key keyCode { get; internal set; }
        public int scanCode { get { return 0; } }
    }

    public class AnyKeyControl : ButtonControl { }

    public class IntegerControl : InputControl<int>
    {
        internal Func<int> now = () => 0;
        internal override int ReadNow() { return now(); }
        internal override int ReadBefore() { return now(); }
    }

    public class Vector2Control : InputControl<Vector2>
    {
        public AxisControl x { get; internal set; }
        public AxisControl y { get; internal set; }

        internal void Build(Func<float> xNow, Func<float> xBefore, Func<float> yNow, Func<float> yBefore)
        {
            x = Add(new AxisControl(), "x", "X");
            y = Add(new AxisControl(), "y", "Y");
            x.now = xNow; x.before = xBefore;
            y.now = yNow; y.before = yBefore;
        }

        internal override Vector2 ReadNow() { return new Vector2(x.ReadNow(), y.ReadNow()); }
        internal override Vector2 ReadBefore() { return new Vector2(x.ReadBefore(), y.ReadBefore()); }
        public override float EvaluateMagnitude() { return ReadValue().magnitude; }
    }

    /// <summary>A stick: x and y, and up / down / left / right as buttons (each the part of the axis on its side).</summary>
    public class StickControl : Vector2Control
    {
        public ButtonControl up { get; internal set; }
        public ButtonControl down { get; internal set; }
        public ButtonControl left { get; internal set; }
        public ButtonControl right { get; internal set; }

        internal void BuildDirections()
        {
            AxisControl ax = x, ay = y;
            up = Add(new ButtonControl(), "up", "Up");
            up.now = () => Mathf.Clamp01(ay.ReadNow()); up.before = () => Mathf.Clamp01(ay.ReadBefore());
            down = Add(new ButtonControl(), "down", "Down");
            down.now = () => Mathf.Clamp01(-ay.ReadNow()); down.before = () => Mathf.Clamp01(-ay.ReadBefore());
            left = Add(new ButtonControl(), "left", "Left");
            left.now = () => Mathf.Clamp01(-ax.ReadNow()); left.before = () => Mathf.Clamp01(-ax.ReadBefore());
            right = Add(new ButtonControl(), "right", "Right");
            right.now = () => Mathf.Clamp01(ax.ReadNow()); right.before = () => Mathf.Clamp01(ax.ReadBefore());
        }
    }

    /// <summary>A d-pad: four buttons; its value points the way they are held (diagonals have length 1, as Unity's).</summary>
    public class DpadControl : Vector2Control
    {
        public ButtonControl up { get; internal set; }
        public ButtonControl down { get; internal set; }
        public ButtonControl left { get; internal set; }
        public ButtonControl right { get; internal set; }

        internal void BuildButtons(ButtonControl u, ButtonControl d, ButtonControl l, ButtonControl r)
        {
            up = u; down = d; left = l; right = r;
            x = Add(new AxisControl(), "x", "X");
            y = Add(new AxisControl(), "y", "Y");
            x.now = () => Unit(r.ReadNow() - l.ReadNow(), u.ReadNow() - d.ReadNow()).x;
            x.before = () => Unit(r.ReadBefore() - l.ReadBefore(), u.ReadBefore() - d.ReadBefore()).x;
            y.now = () => Unit(r.ReadNow() - l.ReadNow(), u.ReadNow() - d.ReadNow()).y;
            y.before = () => Unit(r.ReadBefore() - l.ReadBefore(), u.ReadBefore() - d.ReadBefore()).y;
        }

        static Vector2 Unit(float x, float y)
        {
            Vector2 v = new Vector2(x, y);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
    }

    public enum TouchPhase { None, Began, Moved, Ended, Canceled, Stationary }

    public class TouchControl : InputControl<TouchState>
    {
        public Vector2Control position { get; internal set; }
        public Vector2Control delta { get; internal set; }
        public ButtonControl press { get; internal set; }
        public IntegerControl touchId { get; internal set; }
        public bool isInProgress { get { return false; } }

        internal override TouchState ReadNow() { return default(TouchState); }
        internal override TouchState ReadBefore() { return default(TouchState); }
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    public struct TouchState
    {
        public int touchId;
        public Vector2 position;
        public Vector2 delta;
        public float pressure;
        public UnityEngine.InputSystem.Controls.TouchPhase phase;
    }

    public interface IInputEventTypeInfo { }
    public struct StateEvent : IInputEventTypeInfo { }
    public struct DeltaStateEvent : IInputEventTypeInfo { }

    /// <summary>One frame's change of a device's state (every event here is a StateEvent).</summary>
    public struct InputEventPtr
    {
        internal InputDevice dev;
        internal double at;

        public bool valid { get { return dev != null; } }
        public int deviceId { get { return dev == null ? 0 : dev.deviceId; } }
        public double time { get { return at; } }
        public bool handled { get; set; }
        public bool IsA<TOtherEvent>() where TOtherEvent : struct, IInputEventTypeInfo { return typeof(TOtherEvent) == typeof(StateEvent); }
    }

    public static class InputControlExtensions
    {
        /// <summary>The controls of the device whose value this event changes.</summary>
        public static IEnumerable<InputControl> EnumerateChangedControls(this InputEventPtr eventPtr, InputDevice device = null, float magnitudeThreshold = 0f)
        {
            InputDevice d = device ?? eventPtr.dev;
            if (d == null) yield break;
            foreach (InputControl c in d.controlList)
                if (c.ChangedThisFrame()) yield return c;
        }
    }
}

namespace UnityEngine.InputSystem
{
    public struct InputDeviceDescription
    {
        public string interfaceName;
        public string deviceClass;
        public string manufacturer;
        public string product;
        public string serial;
        public string version;
        public string capabilities;
        public bool empty { get { return string.IsNullOrEmpty(product) && string.IsNullOrEmpty(manufacturer) && string.IsNullOrEmpty(interfaceName); } }
    }

    public enum InputDeviceChange { Added, Removed, Disconnected, Reconnected, Enabled, Disabled, UsageChanged, ConfigurationChanged, SoftReset, HardReset }

    public abstract class InputDevice : InputControl
    {
        internal readonly List<InputControl> controlList = new List<InputControl>();
        static int nextId = 1;

        public int deviceId { get; internal set; }
        public InputDeviceDescription description { get; internal set; }
        public bool enabled { get { return true; } }
        public bool canRunInBackground { get { return false; } }
        public bool added { get; internal set; }
        public bool remote { get { return false; } }
        public bool native { get { return true; } }
        public double lastUpdateTime { get { return Time.realtimeSinceStartup; } }
        public bool wasUpdatedThisFrame { get { return ChangedThisFrame(); } }
        public ReadOnlyArray<InputControl> allControls { get { return new ReadOnlyArray<InputControl>(controlList.ToArray()); } }

        protected InputDevice(string deviceName, string product)
        {
            name = deviceName;
            displayName = product;
            deviceId = nextId++;
            InputDeviceDescription d = new InputDeviceDescription();
            d.interfaceName = "GLFW";
            d.product = product;
            description = d;
        }

        public virtual void MakeCurrent() { }
        public override object ReadValueAsObject() { return null; }
        internal override bool ChangedThisFrame()
        {
            foreach (InputControl c in controlList) if (c.ChangedThisFrame()) return true;
            return false;
        }

        internal ButtonControl Button(string controlName, string display, Func<float> now, Func<float> before)
        {
            ButtonControl b = Add(new ButtonControl(), controlName, display);
            b.now = now;
            b.before = before;
            return b;
        }
    }

    public class Keyboard : InputDevice
    {
        internal static Keyboard instance;
        internal readonly KeyControl[] keyControls = new KeyControl[InputState.KeyCount];

        public static Keyboard current { get { InputSystem.Sync(); return instance; } }

        static readonly string[] KeyNames = {
            "Space", "Enter", "Tab", "Backquote", "Quote", "Semicolon", "Comma", "Period", "Slash", "Backslash", "LeftBracket", "RightBracket",
            "Minus", "Equals", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J",
            "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V",
            "W", "X", "Y", "Z", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8",
            "Digit9", "Digit0", "LeftShift", "RightShift", "LeftAlt", "RightAlt", "LeftCtrl", "RightCtrl", "LeftMeta", "RightMeta", "ContextMenu", "Escape",
            "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "Backspace", "PageDown", "PageUp", "Home", "End", "Insert", "Delete", "CapsLock",
            "NumLock", "PrintScreen", "ScrollLock", "Pause", "NumpadEnter", "NumpadDivide", "NumpadMultiply", "NumpadPlus", "NumpadMinus", "NumpadPeriod", "NumpadEquals", "Numpad0",
            "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9", "F1", "F2", "F3",
            "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "OEM1", "OEM2", "OEM3",
            "OEM4", "OEM5", "IMESelected"
        };


        public Keyboard() : base("Keyboard", "Keyboard")
        {
            for (int key = 1; key < InputState.KeyCount; key++)
            {
                int i = key;
                string n = KeyNames[i - 1];
                KeyControl c = Add(new KeyControl(), n.Substring(0, 1).ToLowerInvariant() + n.Substring(1), n);
                c.keyCode = (Key)i;
                c.now = () => InputState.keys[i] ? 1f : 0f;
                c.before = () => InputState.keysBefore[i] ? 1f : 0f;
                keyControls[i] = c;
            }
            anyKey = Add(new AnyKeyControl(), "anyKey", "Any Key");
            anyKey.now = () => Any(InputState.keys);
            anyKey.before = () => Any(InputState.keysBefore);
            shiftKey = Either(Key.LeftShift, Key.RightShift, "shift", "Shift");
            ctrlKey = Either(Key.LeftCtrl, Key.RightCtrl, "ctrl", "Control");
            altKey = Either(Key.LeftAlt, Key.RightAlt, "alt", "Alt");
        }

        static float Any(bool[] held)
        {
            for (int i = 1; i < held.Length; i++) if (held[i]) return 1f;
            return 0f;
        }

        ButtonControl Either(Key a, Key b, string controlName, string display)
        {
            int ia = (int)a, ib = (int)b;
            return Button(controlName, display, () => InputState.keys[ia] || InputState.keys[ib] ? 1f : 0f,
                () => InputState.keysBefore[ia] || InputState.keysBefore[ib] ? 1f : 0f);
        }

        public KeyControl this[Key key] { get { return keyControls[(int)key]; } }
        public AnyKeyControl anyKey { get; private set; }
        public ButtonControl shiftKey { get; private set; }
        public ButtonControl ctrlKey { get; private set; }
        public ButtonControl altKey { get; private set; }
        public KeyControl leftWindowsKey { get { return keyControls[(int)Key.LeftMeta]; } }
        public KeyControl rightWindowsKey { get { return keyControls[(int)Key.RightMeta]; } }
        public KeyControl leftAppleKey { get { return keyControls[(int)Key.LeftMeta]; } }
        public KeyControl rightAppleKey { get { return keyControls[(int)Key.RightMeta]; } }
        public KeyControl leftCommandKey { get { return keyControls[(int)Key.LeftMeta]; } }
        public KeyControl rightCommandKey { get { return keyControls[(int)Key.RightMeta]; } }
        public KeyControl altGrKey { get { return keyControls[(int)Key.RightAlt]; } }
        public KeyControl spaceKey { get { return keyControls[(int)Key.Space]; } }
        public KeyControl enterKey { get { return keyControls[(int)Key.Enter]; } }
        public KeyControl tabKey { get { return keyControls[(int)Key.Tab]; } }
        public KeyControl backquoteKey { get { return keyControls[(int)Key.Backquote]; } }
        public KeyControl quoteKey { get { return keyControls[(int)Key.Quote]; } }
        public KeyControl semicolonKey { get { return keyControls[(int)Key.Semicolon]; } }
        public KeyControl commaKey { get { return keyControls[(int)Key.Comma]; } }
        public KeyControl periodKey { get { return keyControls[(int)Key.Period]; } }
        public KeyControl slashKey { get { return keyControls[(int)Key.Slash]; } }
        public KeyControl backslashKey { get { return keyControls[(int)Key.Backslash]; } }
        public KeyControl leftBracketKey { get { return keyControls[(int)Key.LeftBracket]; } }
        public KeyControl rightBracketKey { get { return keyControls[(int)Key.RightBracket]; } }
        public KeyControl minusKey { get { return keyControls[(int)Key.Minus]; } }
        public KeyControl equalsKey { get { return keyControls[(int)Key.Equals]; } }
        public KeyControl aKey { get { return keyControls[(int)Key.A]; } }
        public KeyControl bKey { get { return keyControls[(int)Key.B]; } }
        public KeyControl cKey { get { return keyControls[(int)Key.C]; } }
        public KeyControl dKey { get { return keyControls[(int)Key.D]; } }
        public KeyControl eKey { get { return keyControls[(int)Key.E]; } }
        public KeyControl fKey { get { return keyControls[(int)Key.F]; } }
        public KeyControl gKey { get { return keyControls[(int)Key.G]; } }
        public KeyControl hKey { get { return keyControls[(int)Key.H]; } }
        public KeyControl iKey { get { return keyControls[(int)Key.I]; } }
        public KeyControl jKey { get { return keyControls[(int)Key.J]; } }
        public KeyControl kKey { get { return keyControls[(int)Key.K]; } }
        public KeyControl lKey { get { return keyControls[(int)Key.L]; } }
        public KeyControl mKey { get { return keyControls[(int)Key.M]; } }
        public KeyControl nKey { get { return keyControls[(int)Key.N]; } }
        public KeyControl oKey { get { return keyControls[(int)Key.O]; } }
        public KeyControl pKey { get { return keyControls[(int)Key.P]; } }
        public KeyControl qKey { get { return keyControls[(int)Key.Q]; } }
        public KeyControl rKey { get { return keyControls[(int)Key.R]; } }
        public KeyControl sKey { get { return keyControls[(int)Key.S]; } }
        public KeyControl tKey { get { return keyControls[(int)Key.T]; } }
        public KeyControl uKey { get { return keyControls[(int)Key.U]; } }
        public KeyControl vKey { get { return keyControls[(int)Key.V]; } }
        public KeyControl wKey { get { return keyControls[(int)Key.W]; } }
        public KeyControl xKey { get { return keyControls[(int)Key.X]; } }
        public KeyControl yKey { get { return keyControls[(int)Key.Y]; } }
        public KeyControl zKey { get { return keyControls[(int)Key.Z]; } }
        public KeyControl digit1Key { get { return keyControls[(int)Key.Digit1]; } }
        public KeyControl digit2Key { get { return keyControls[(int)Key.Digit2]; } }
        public KeyControl digit3Key { get { return keyControls[(int)Key.Digit3]; } }
        public KeyControl digit4Key { get { return keyControls[(int)Key.Digit4]; } }
        public KeyControl digit5Key { get { return keyControls[(int)Key.Digit5]; } }
        public KeyControl digit6Key { get { return keyControls[(int)Key.Digit6]; } }
        public KeyControl digit7Key { get { return keyControls[(int)Key.Digit7]; } }
        public KeyControl digit8Key { get { return keyControls[(int)Key.Digit8]; } }
        public KeyControl digit9Key { get { return keyControls[(int)Key.Digit9]; } }
        public KeyControl digit0Key { get { return keyControls[(int)Key.Digit0]; } }
        public KeyControl leftShiftKey { get { return keyControls[(int)Key.LeftShift]; } }
        public KeyControl rightShiftKey { get { return keyControls[(int)Key.RightShift]; } }
        public KeyControl leftAltKey { get { return keyControls[(int)Key.LeftAlt]; } }
        public KeyControl rightAltKey { get { return keyControls[(int)Key.RightAlt]; } }
        public KeyControl leftCtrlKey { get { return keyControls[(int)Key.LeftCtrl]; } }
        public KeyControl rightCtrlKey { get { return keyControls[(int)Key.RightCtrl]; } }
        public KeyControl leftMetaKey { get { return keyControls[(int)Key.LeftMeta]; } }
        public KeyControl rightMetaKey { get { return keyControls[(int)Key.RightMeta]; } }
        public KeyControl contextMenuKey { get { return keyControls[(int)Key.ContextMenu]; } }
        public KeyControl escapeKey { get { return keyControls[(int)Key.Escape]; } }
        public KeyControl leftArrowKey { get { return keyControls[(int)Key.LeftArrow]; } }
        public KeyControl rightArrowKey { get { return keyControls[(int)Key.RightArrow]; } }
        public KeyControl upArrowKey { get { return keyControls[(int)Key.UpArrow]; } }
        public KeyControl downArrowKey { get { return keyControls[(int)Key.DownArrow]; } }
        public KeyControl backspaceKey { get { return keyControls[(int)Key.Backspace]; } }
        public KeyControl pageDownKey { get { return keyControls[(int)Key.PageDown]; } }
        public KeyControl pageUpKey { get { return keyControls[(int)Key.PageUp]; } }
        public KeyControl homeKey { get { return keyControls[(int)Key.Home]; } }
        public KeyControl endKey { get { return keyControls[(int)Key.End]; } }
        public KeyControl insertKey { get { return keyControls[(int)Key.Insert]; } }
        public KeyControl deleteKey { get { return keyControls[(int)Key.Delete]; } }
        public KeyControl capsLockKey { get { return keyControls[(int)Key.CapsLock]; } }
        public KeyControl numLockKey { get { return keyControls[(int)Key.NumLock]; } }
        public KeyControl printScreenKey { get { return keyControls[(int)Key.PrintScreen]; } }
        public KeyControl scrollLockKey { get { return keyControls[(int)Key.ScrollLock]; } }
        public KeyControl pauseKey { get { return keyControls[(int)Key.Pause]; } }
        public KeyControl numpadEnterKey { get { return keyControls[(int)Key.NumpadEnter]; } }
        public KeyControl numpadDivideKey { get { return keyControls[(int)Key.NumpadDivide]; } }
        public KeyControl numpadMultiplyKey { get { return keyControls[(int)Key.NumpadMultiply]; } }
        public KeyControl numpadPlusKey { get { return keyControls[(int)Key.NumpadPlus]; } }
        public KeyControl numpadMinusKey { get { return keyControls[(int)Key.NumpadMinus]; } }
        public KeyControl numpadPeriodKey { get { return keyControls[(int)Key.NumpadPeriod]; } }
        public KeyControl numpadEqualsKey { get { return keyControls[(int)Key.NumpadEquals]; } }
        public KeyControl numpad0Key { get { return keyControls[(int)Key.Numpad0]; } }
        public KeyControl numpad1Key { get { return keyControls[(int)Key.Numpad1]; } }
        public KeyControl numpad2Key { get { return keyControls[(int)Key.Numpad2]; } }
        public KeyControl numpad3Key { get { return keyControls[(int)Key.Numpad3]; } }
        public KeyControl numpad4Key { get { return keyControls[(int)Key.Numpad4]; } }
        public KeyControl numpad5Key { get { return keyControls[(int)Key.Numpad5]; } }
        public KeyControl numpad6Key { get { return keyControls[(int)Key.Numpad6]; } }
        public KeyControl numpad7Key { get { return keyControls[(int)Key.Numpad7]; } }
        public KeyControl numpad8Key { get { return keyControls[(int)Key.Numpad8]; } }
        public KeyControl numpad9Key { get { return keyControls[(int)Key.Numpad9]; } }
        public KeyControl f1Key { get { return keyControls[(int)Key.F1]; } }
        public KeyControl f2Key { get { return keyControls[(int)Key.F2]; } }
        public KeyControl f3Key { get { return keyControls[(int)Key.F3]; } }
        public KeyControl f4Key { get { return keyControls[(int)Key.F4]; } }
        public KeyControl f5Key { get { return keyControls[(int)Key.F5]; } }
        public KeyControl f6Key { get { return keyControls[(int)Key.F6]; } }
        public KeyControl f7Key { get { return keyControls[(int)Key.F7]; } }
        public KeyControl f8Key { get { return keyControls[(int)Key.F8]; } }
        public KeyControl f9Key { get { return keyControls[(int)Key.F9]; } }
        public KeyControl f10Key { get { return keyControls[(int)Key.F10]; } }
        public KeyControl f11Key { get { return keyControls[(int)Key.F11]; } }
        public KeyControl f12Key { get { return keyControls[(int)Key.F12]; } }
        public KeyControl oem1Key { get { return keyControls[(int)Key.OEM1]; } }
        public KeyControl oem2Key { get { return keyControls[(int)Key.OEM2]; } }
        public KeyControl oem3Key { get { return keyControls[(int)Key.OEM3]; } }
        public KeyControl oem4Key { get { return keyControls[(int)Key.OEM4]; } }
        public KeyControl oem5Key { get { return keyControls[(int)Key.OEM5]; } }
    }

    public class Pointer : InputDevice
    {
        public Vector2Control position { get; internal set; }
        public Vector2Control delta { get; internal set; }
        public ButtonControl press { get; internal set; }

        public static Pointer current { get { return Mouse.current; } }

        protected Pointer(string deviceName, string product) : base(deviceName, product) { }
    }

    public class Mouse : Pointer
    {
        internal static Mouse instance;

        public static Mouse current { get { InputSystem.Sync(); return instance; } }

        public Vector2Control scroll { get; private set; }
        public ButtonControl leftButton { get; private set; }
        public ButtonControl rightButton { get; private set; }
        public ButtonControl middleButton { get; private set; }
        public ButtonControl backButton { get; private set; }
        public ButtonControl forwardButton { get; private set; }

        public Mouse() : base("Mouse", "Mouse")
        {
            position = Add(new Vector2Control(), "position", "Position");
            position.Build(() => InputState.pointerX, () => InputState.pointerXBefore, () => InputState.pointerY, () => InputState.pointerYBefore);
            delta = Add(new Vector2Control(), "delta", "Delta");
            delta.Build(() => InputState.pointerX - InputState.pointerXBefore, () => 0f, () => InputState.pointerY - InputState.pointerYBefore, () => 0f);
            // a wheel notch is 120 in Unity's Mouse.scroll (the Windows WHEEL_DELTA): the host reports notches
            scroll = Add(new Vector2Control(), "scroll", "Scroll");
            scroll.Build(() => InputState.scrollX * 120f, () => 0f, () => InputState.scrollY * 120f, () => 0f);
            leftButton = Bit("leftButton", "Left Button", 1);
            rightButton = Bit("rightButton", "Right Button", 2);
            middleButton = Bit("middleButton", "Middle Button", 4);
            backButton = Bit("backButton", "Back Button", 8);
            forwardButton = Bit("forwardButton", "Forward Button", 16);
            press = Button("press", "Press", () => InputState.buttons != 0 ? 1f : 0f, () => InputState.buttonsBefore != 0 ? 1f : 0f);
        }

        ButtonControl Bit(string controlName, string display, int bit)
        {
            return Button(controlName, display, () => (InputState.buttons & bit) != 0 ? 1f : 0f, () => (InputState.buttonsBefore & bit) != 0 ? 1f : 0f);
        }

        /// <summary>Moves the pointer (the host is not told: the next frame's report wins).</summary>
        public void WarpCursorPosition(Vector2 p) { InputState.pointerX = p.x; InputState.pointerY = p.y; }
    }

    public class Gamepad : InputDevice
    {
        internal static readonly List<Gamepad> list = new List<Gamepad>();
        internal readonly int pad;

        public static Gamepad current { get { InputSystem.Sync(); return list.Count > 0 ? list[0] : null; } }
        public static ReadOnlyArray<Gamepad> all { get { InputSystem.Sync(); return new ReadOnlyArray<Gamepad>(list.ToArray()); } }

        public ButtonControl buttonSouth { get; private set; }
        public ButtonControl buttonEast { get; private set; }
        public ButtonControl buttonWest { get; private set; }
        public ButtonControl buttonNorth { get; private set; }
        public ButtonControl leftShoulder { get; private set; }
        public ButtonControl rightShoulder { get; private set; }
        public ButtonControl leftTrigger { get; private set; }
        public ButtonControl rightTrigger { get; private set; }
        public ButtonControl selectButton { get; private set; }
        public ButtonControl startButton { get; private set; }
        public ButtonControl leftStickButton { get; private set; }
        public ButtonControl rightStickButton { get; private set; }
        public StickControl leftStick { get; private set; }
        public StickControl rightStick { get; private set; }
        public DpadControl dpad { get; private set; }

        public ButtonControl aButton { get { return buttonSouth; } }
        public ButtonControl bButton { get { return buttonEast; } }
        public ButtonControl xButton { get { return buttonWest; } }
        public ButtonControl yButton { get { return buttonNorth; } }
        public ButtonControl crossButton { get { return buttonSouth; } }
        public ButtonControl circleButton { get { return buttonEast; } }
        public ButtonControl squareButton { get { return buttonWest; } }
        public ButtonControl triangleButton { get { return buttonNorth; } }
        public ButtonControl this[GamepadButton button]
        {
            get
            {
                switch (button)
                {
                    case GamepadButton.North: return buttonNorth;
                    case GamepadButton.East: return buttonEast;
                    case GamepadButton.South: return buttonSouth;
                    case GamepadButton.West: return buttonWest;
                    case GamepadButton.LeftShoulder: return leftShoulder;
                    case GamepadButton.RightShoulder: return rightShoulder;
                    case GamepadButton.LeftTrigger: return leftTrigger;
                    case GamepadButton.RightTrigger: return rightTrigger;
                    case GamepadButton.Start: return startButton;
                    case GamepadButton.Select: return selectButton;
                    case GamepadButton.LeftStick: return leftStickButton;
                    case GamepadButton.RightStick: return rightStickButton;
                    case GamepadButton.DpadUp: return dpad.up;
                    case GamepadButton.DpadDown: return dpad.down;
                    case GamepadButton.DpadLeft: return dpad.left;
                    default: return dpad.right;
                }
            }
        }

        internal Gamepad(int pad) : base("Gamepad" + (pad == 0 ? "" : pad.ToString()), "Gamepad")
        {
            this.pad = pad;
            buttonSouth = PadButton("buttonSouth", "A", 0);
            buttonEast = PadButton("buttonEast", "B", 1);
            buttonWest = PadButton("buttonWest", "X", 2);
            buttonNorth = PadButton("buttonNorth", "Y", 3);
            leftShoulder = PadButton("leftShoulder", "Left Shoulder", 4);
            rightShoulder = PadButton("rightShoulder", "Right Shoulder", 5);
            selectButton = PadButton("select", "Select", 6);
            startButton = PadButton("start", "Start", 7);
            leftStickButton = PadButton("leftStickPress", "Left Stick Press", 9);
            rightStickButton = PadButton("rightStickPress", "Right Stick Press", 10);
            ButtonControl du = PadButton("dpad/up", "D-Pad Up", 11), dr = PadButton("dpad/right", "D-Pad Right", 12),
                dd = PadButton("dpad/down", "D-Pad Down", 13), dl = PadButton("dpad/left", "D-Pad Left", 14);
            kids.Remove(du); kids.Remove(dr); kids.Remove(dd); kids.Remove(dl);
            dpad = Add(new DpadControl(), "dpad", "D-Pad");
            du.name = "up"; dd.name = "down"; dl.name = "left"; dr.name = "right";
            foreach (ButtonControl b in new[] { du, dd, dl, dr }) { b.parent = dpad; dpad.kids.Add(b); }
            dpad.BuildButtons(du, dd, dl, dr);
            leftTrigger = Button("leftTrigger", "Left Trigger", () => Axis(4), () => AxisBefore(4));
            rightTrigger = Button("rightTrigger", "Right Trigger", () => Axis(5), () => AxisBefore(5));
            leftStick = Stick("leftStick", "Left Stick", 0);
            rightStick = Stick("rightStick", "Right Stick", 2);
        }

        float Axis(int a) { return InputState.padAxes[pad * InputState.PadAxes + a]; }
        float AxisBefore(int a) { return InputState.padAxesBefore[pad * InputState.PadAxes + a]; }

        ButtonControl PadButton(string controlName, string display, int b)
        {
            int k = pad * InputState.PadButtons + b;
            return Button(controlName, display, () => InputState.padButtons[k] ? 1f : 0f, () => InputState.padButtonsBefore[k] ? 1f : 0f);
        }

        StickControl Stick(string controlName, string display, int axis)
        {
            StickControl s = Add(new StickControl(), controlName, display);
            s.Build(() => Axis(axis), () => AxisBefore(axis), () => Axis(axis + 1), () => AxisBefore(axis + 1));
            s.BuildDirections();
            return s;
        }

        public void SetMotorSpeeds(float lowFrequency, float highFrequency) { }
        public void PauseHaptics() { }
        public void ResumeHaptics() { }
        public void ResetHaptics() { }
    }

    public enum GamepadButton { DpadUp, DpadDown, DpadLeft, DpadRight, North, East, South, West, LeftStick, RightStick, LeftShoulder, RightShoulder, Start, Select,
        LeftTrigger = 32, RightTrigger = 33, X = West, Y = North, A = South, B = East, Cross = South, Square = West, Triangle = North, Circle = East }

    /// <summary>A joystick that is not a gamepad.  The host reports none.</summary>
    public class Joystick : InputDevice
    {
        internal static readonly List<Joystick> list = new List<Joystick>();

        public static Joystick current { get { return list.Count > 0 ? list[0] : null; } }
        public static ReadOnlyArray<Joystick> all { get { return new ReadOnlyArray<Joystick>(list.ToArray()); } }

        public ButtonControl trigger { get; protected set; }
        public StickControl stick { get; protected set; }
        public AxisControl twist { get; protected set; }
        public DpadControl hatswitch { get; protected set; }

        protected Joystick() : base("Joystick", "Joystick") { }
    }

    /// <summary>A touch screen.  The host reports none, so current is null (as in Unity on a desktop).</summary>
    public class Touchscreen : Pointer
    {
        public static Touchscreen current { get { return null; } }

        public TouchControl primaryTouch { get; protected set; }
        public ReadOnlyArray<TouchControl> touches { get; protected set; }

        protected Touchscreen() : base("Touchscreen", "Touchscreen") { }
    }

    /// <summary>The Input System's settings asset (a script field may hold one).</summary>
    public class InputSettings : ScriptableObject
    {
        public float defaultDeadzoneMin = 0.125f;
        public float defaultDeadzoneMax = 0.925f;
        public float defaultButtonPressPoint = 0.5f;
        public float buttonReleaseThreshold = 0.75f;
        public float defaultTapTime = 0.2f;
        public float defaultSlowTapTime = 0.5f;
        public float defaultHoldTime = 0.4f;
    }

    public enum InputActionPhase { Disabled, Waiting, Started, Performed, Canceled }
    public enum InputActionType { Value, Button, PassThrough }

    /// <summary>An input action.  Its bindings are serialized data the packer does not write, so it never starts, performs or cancels.</summary>
    public class InputAction
    {
        public struct CallbackContext
        {
            internal InputAction act;

            public InputAction action { get { return act; } }
            public InputActionPhase phase { get { return act == null ? InputActionPhase.Disabled : act.phase; } }
            public bool started { get { return phase == InputActionPhase.Started; } }
            public bool performed { get { return phase == InputActionPhase.Performed; } }
            public bool canceled { get { return phase == InputActionPhase.Canceled; } }
            public InputControl control { get { return null; } }
            public double time { get { return Time.realtimeSinceStartup; } }
            public TValue ReadValue<TValue>() where TValue : struct { return default(TValue); }
            public bool ReadValueAsButton() { return false; }
            public object ReadValueAsObject() { return null; }
        }

        public string name { get; private set; }
        public InputActionType type { get; private set; }
        public bool enabled { get; private set; }
        public InputActionPhase phase { get { return enabled ? InputActionPhase.Waiting : InputActionPhase.Disabled; } }
        public bool triggered { get { return false; } }

        public event Action<CallbackContext> started;
        public event Action<CallbackContext> performed;
        public event Action<CallbackContext> canceled;

        public InputAction() { name = ""; }
        public InputAction(string name = null, InputActionType type = InputActionType.Value, string binding = null) { this.name = name ?? ""; this.type = type; }

        public void Enable() { enabled = true; }
        public void Disable() { enabled = false; }
        public TValue ReadValue<TValue>() where TValue : struct { return default(TValue); }
        public bool IsPressed() { return false; }
        public bool WasPressedThisFrame() { return false; }
        public bool WasReleasedThisFrame() { return false; }
        public bool WasPerformedThisFrame() { return false; }

        internal void Raise(InputActionPhase p)
        {
            CallbackContext c = new CallbackContext();
            c.act = this;
            if (p == InputActionPhase.Started && started != null) started(c);
            if (p == InputActionPhase.Performed && performed != null) performed(c);
            if (p == InputActionPhase.Canceled && canceled != null) canceled(c);
        }
    }

    public static class InputSystem
    {
        static readonly List<InputDevice> list = new List<InputDevice>();
        static int syncedFrame = -1;
        internal static bool dispatching;

        public static InputSettings settings { get; set; }
        public static ReadOnlyArray<InputDevice> devices { get { Sync(); return new ReadOnlyArray<InputDevice>(list.ToArray()); } }
        public static event Action<InputDevice, InputDeviceChange> onDeviceChange;
        public static event Action<InputEventPtr, InputDevice> onEvent;
        public static string version { get { return "1.7.0"; } }

        /// <summary>The devices as the host reports them this frame: one is added (onDeviceChange Added) when it appears and removed when it goes.</summary>
        internal static void Sync()
        {
            if (syncedFrame == Time.frameCount) return;
            syncedFrame = Time.frameCount;
            if (InputState.keyboard && Keyboard.instance == null) Keyboard.instance = (Keyboard)AddDevice(new Keyboard());
            if (!InputState.keyboard && Keyboard.instance != null) { RemoveDevice(Keyboard.instance); Keyboard.instance = null; }
            if (InputState.mouse && Mouse.instance == null) Mouse.instance = (Mouse)AddDevice(new Mouse());
            for (int p = 0; p < InputState.MaxPads; p++)
            {
                Gamepad have = null;
                foreach (Gamepad g in Gamepad.list) if (g.pad == p) have = g;
                if (InputState.pads[p] && have == null) Gamepad.list.Add((Gamepad)AddDevice(new Gamepad(p)));
                if (!InputState.pads[p] && have != null) { Gamepad.list.Remove(have); RemoveDevice(have); }
            }
        }

        public static InputDevice AddDevice(InputDevice device)
        {
            list.Add(device);
            device.added = true;
            if (onDeviceChange != null) onDeviceChange(device, InputDeviceChange.Added);
            return device;
        }

        public static void RemoveDevice(InputDevice device)
        {
            list.Remove(device);
            device.added = false;
            if (onDeviceChange != null) onDeviceChange(device, InputDeviceChange.Removed);
        }

        public static InputDevice GetDeviceById(int deviceId)
        {
            foreach (InputDevice d in list) if (d.deviceId == deviceId) return d;
            return null;
        }

        /// <summary>The engine calls this at the start of each frame: devices are synced and onEvent is told of each one whose state changed.</summary>
        public static void Update()
        {
            Sync();
            if (onEvent == null) return;
            foreach (InputDevice d in list.ToArray())
            {
                if (!d.ChangedThisFrame()) continue;
                InputEventPtr e = new InputEventPtr();
                e.dev = d;
                e.at = Time.realtimeSinceStartup;
                dispatching = true;
                try { onEvent(e, d); }
                finally { dispatching = false; }
            }
        }
    }
}

namespace UnityEngine
{
    /// <summary>Unity's KeyCode (the old Input class's keys), with Unity's numbers.</summary>
    public enum KeyCode
    {
        None = 0, Backspace = 8, Tab = 9, Clear = 12, Return = 13, Pause = 19, Escape = 27, Space = 32, Quote = 39, Comma = 44, Minus = 45, Period = 46,
        Slash = 47, Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Alpha8 = 56, Alpha9 = 57,
        Semicolon = 59, Equals = 61, LeftBracket = 91, Backslash = 92, RightBracket = 93, BackQuote = 96,
        A = 97, B = 98, C = 99, D = 100, E = 101, F = 102, G = 103, H = 104, I = 105, J = 106, K = 107, L = 108, M = 109, N = 110, O = 111, P = 112, Q = 113,
        R = 114, S = 115, T = 116, U = 117, V = 118, W = 119, X = 120, Y = 121, Z = 122, Delete = 127,
        Keypad0 = 256, Keypad1 = 257, Keypad2 = 258, Keypad3 = 259, Keypad4 = 260, Keypad5 = 261, Keypad6 = 262, Keypad7 = 263, Keypad8 = 264, Keypad9 = 265,
        KeypadPeriod = 266, KeypadDivide = 267, KeypadMultiply = 268, KeypadMinus = 269, KeypadPlus = 270, KeypadEnter = 271, KeypadEquals = 272,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276, Insert = 277, Home = 278, End = 279, PageUp = 280, PageDown = 281,
        F1 = 282, F2 = 283, F3 = 284, F4 = 285, F5 = 286, F6 = 287, F7 = 288, F8 = 289, F9 = 290, F10 = 291, F11 = 292, F12 = 293,
        Numlock = 300, CapsLock = 301, ScrollLock = 302, RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306, RightAlt = 307, LeftAlt = 308,
        RightCommand = 309, RightApple = 309, LeftCommand = 310, LeftApple = 310, LeftWindows = 311, RightWindows = 312, AltGr = 313, Print = 316, Menu = 319,
        Mouse0 = 323, Mouse1 = 324, Mouse2 = 325, Mouse3 = 326, Mouse4 = 327, Mouse5 = 328, Mouse6 = 329,
        JoystickButton0 = 330, JoystickButton1 = 331, JoystickButton2 = 332, JoystickButton3 = 333, JoystickButton4 = 334, JoystickButton5 = 335,
        JoystickButton6 = 336, JoystickButton7 = 337, JoystickButton8 = 338, JoystickButton9 = 339
    }

    /// <summary>The old input API, over the same state.  GetAxis is GetAxisRaw (no smoothing); the axes are Unity's default ones.</summary>
    public static class Input
    {
        static bool Held(KeyCode k, bool before)
        {
            int code = (int)k;
            if (code >= 323 && code <= 327) return ((before ? InputState.buttonsBefore : InputState.buttons) & (1 << (code - 323))) != 0;
            if (code >= 330 && code <= 339)
            {
                int b = code - 330;
                return b < InputState.PadButtons && (before ? InputState.padButtonsBefore : InputState.padButtons)[b];
            }
            int key = KeyIndex(k);
            return key > 0 && (before ? InputState.keysBefore : InputState.keys)[key];
        }

        /// <summary>The Input System's Key of a KeyCode (0 for none).</summary>
        static int KeyIndex(KeyCode k)
        {
            int c = (int)k;
            if (c >= 97 && c <= 122) return (int)UnityEngine.InputSystem.Key.A + (c - 97);
            if (c >= 49 && c <= 57) return (int)UnityEngine.InputSystem.Key.Digit1 + (c - 49);
            if (c >= 256 && c <= 265) return (int)UnityEngine.InputSystem.Key.Numpad0 + (c - 256);
            if (c >= 282 && c <= 293) return (int)UnityEngine.InputSystem.Key.F1 + (c - 282);
            switch (k)
            {
                case KeyCode.Alpha0: return (int)UnityEngine.InputSystem.Key.Digit0;
                case KeyCode.Space: return (int)UnityEngine.InputSystem.Key.Space;
                case KeyCode.Return: return (int)UnityEngine.InputSystem.Key.Enter;
                case KeyCode.Tab: return (int)UnityEngine.InputSystem.Key.Tab;
                case KeyCode.Escape: return (int)UnityEngine.InputSystem.Key.Escape;
                case KeyCode.Backspace: return (int)UnityEngine.InputSystem.Key.Backspace;
                case KeyCode.Delete: return (int)UnityEngine.InputSystem.Key.Delete;
                case KeyCode.UpArrow: return (int)UnityEngine.InputSystem.Key.UpArrow;
                case KeyCode.DownArrow: return (int)UnityEngine.InputSystem.Key.DownArrow;
                case KeyCode.LeftArrow: return (int)UnityEngine.InputSystem.Key.LeftArrow;
                case KeyCode.RightArrow: return (int)UnityEngine.InputSystem.Key.RightArrow;
                case KeyCode.LeftShift: return (int)UnityEngine.InputSystem.Key.LeftShift;
                case KeyCode.RightShift: return (int)UnityEngine.InputSystem.Key.RightShift;
                case KeyCode.LeftControl: return (int)UnityEngine.InputSystem.Key.LeftCtrl;
                case KeyCode.RightControl: return (int)UnityEngine.InputSystem.Key.RightCtrl;
                case KeyCode.LeftAlt: return (int)UnityEngine.InputSystem.Key.LeftAlt;
                case KeyCode.RightAlt: return (int)UnityEngine.InputSystem.Key.RightAlt;
                case KeyCode.Insert: return (int)UnityEngine.InputSystem.Key.Insert;
                case KeyCode.Home: return (int)UnityEngine.InputSystem.Key.Home;
                case KeyCode.End: return (int)UnityEngine.InputSystem.Key.End;
                case KeyCode.PageUp: return (int)UnityEngine.InputSystem.Key.PageUp;
                case KeyCode.PageDown: return (int)UnityEngine.InputSystem.Key.PageDown;
                case KeyCode.Comma: return (int)UnityEngine.InputSystem.Key.Comma;
                case KeyCode.Period: return (int)UnityEngine.InputSystem.Key.Period;
                case KeyCode.Slash: return (int)UnityEngine.InputSystem.Key.Slash;
                case KeyCode.Minus: return (int)UnityEngine.InputSystem.Key.Minus;
                case KeyCode.Equals: return (int)UnityEngine.InputSystem.Key.Equals;
                case KeyCode.Semicolon: return (int)UnityEngine.InputSystem.Key.Semicolon;
                case KeyCode.Quote: return (int)UnityEngine.InputSystem.Key.Quote;
                case KeyCode.BackQuote: return (int)UnityEngine.InputSystem.Key.Backquote;
                case KeyCode.LeftBracket: return (int)UnityEngine.InputSystem.Key.LeftBracket;
                case KeyCode.RightBracket: return (int)UnityEngine.InputSystem.Key.RightBracket;
                case KeyCode.Backslash: return (int)UnityEngine.InputSystem.Key.Backslash;
                case KeyCode.CapsLock: return (int)UnityEngine.InputSystem.Key.CapsLock;
                default: return 0;
            }
        }

        static KeyCode Named(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Length == 1 && n[0] >= 'a' && n[0] <= 'z') return (KeyCode)((int)KeyCode.A + (n[0] - 'a'));
            if (n.Length == 1 && n[0] >= '0' && n[0] <= '9') return (KeyCode)((int)KeyCode.Alpha0 + (n[0] - '0'));
            switch (n)
            {
                case "space": return KeyCode.Space;
                case "return": case "enter": return KeyCode.Return;
                case "escape": return KeyCode.Escape;
                case "tab": return KeyCode.Tab;
                case "backspace": return KeyCode.Backspace;
                case "up": return KeyCode.UpArrow;
                case "down": return KeyCode.DownArrow;
                case "left": return KeyCode.LeftArrow;
                case "right": return KeyCode.RightArrow;
                case "left shift": return KeyCode.LeftShift;
                case "right shift": return KeyCode.RightShift;
                case "left ctrl": return KeyCode.LeftControl;
                case "right ctrl": return KeyCode.RightControl;
                case "left alt": return KeyCode.LeftAlt;
                case "right alt": return KeyCode.RightAlt;
                default: return KeyCode.None;
            }
        }

        public static bool GetKey(KeyCode key) { return Held(key, false); }
        public static bool GetKeyDown(KeyCode key) { return Held(key, false) && !Held(key, true); }
        public static bool GetKeyUp(KeyCode key) { return !Held(key, false) && Held(key, true); }
        public static bool GetKey(string name) { return GetKey(Named(name)); }
        public static bool GetKeyDown(string name) { return GetKeyDown(Named(name)); }
        public static bool GetKeyUp(string name) { return GetKeyUp(Named(name)); }

        public static bool GetMouseButton(int button) { return (InputState.buttons & (1 << button)) != 0; }
        public static bool GetMouseButtonDown(int button) { return GetMouseButton(button) && (InputState.buttonsBefore & (1 << button)) == 0; }
        public static bool GetMouseButtonUp(int button) { return !GetMouseButton(button) && (InputState.buttonsBefore & (1 << button)) != 0; }

        public static Vector3 mousePosition { get { return new Vector3(InputState.pointerX, InputState.pointerY, 0f); } }
        public static Vector2 mouseScrollDelta { get { return new Vector2(InputState.scrollX, InputState.scrollY); } }
        public static bool mousePresent { get { return InputState.mouse; } }
        public static bool anyKey
        {
            get
            {
                if (InputState.buttons != 0) return true;
                for (int i = 1; i < InputState.KeyCount; i++) if (InputState.keys[i]) return true;
                return false;
            }
        }
        public static bool anyKeyDown
        {
            get
            {
                if ((InputState.buttons & ~InputState.buttonsBefore) != 0) return true;
                for (int i = 1; i < InputState.KeyCount; i++) if (InputState.keys[i] && !InputState.keysBefore[i]) return true;
                return false;
            }
        }
        public static int touchCount { get { return 0; } }
        public static bool touchSupported { get { return false; } }

        static float Pair(KeyCode neg, KeyCode neg2, KeyCode pos, KeyCode pos2)
        {
            float v = 0f;
            if (GetKey(pos) || GetKey(pos2)) v += 1f;
            if (GetKey(neg) || GetKey(neg2)) v -= 1f;
            return v;
        }

        /// <summary>Unity's default axes: Horizontal and Vertical (arrows, WASD and the first pad's left stick), Mouse X / Y and Mouse ScrollWheel.</summary>
        public static float GetAxisRaw(string axisName)
        {
            switch (axisName)
            {
                case "Horizontal":
                {
                    float k = Pair(KeyCode.LeftArrow, KeyCode.A, KeyCode.RightArrow, KeyCode.D);
                    float s = InputState.pads[0] ? InputState.padAxes[0] : 0f;
                    return k != 0f ? k : (Mathf.Abs(s) > 0.19f ? s : 0f);
                }
                case "Vertical":
                {
                    float k = Pair(KeyCode.DownArrow, KeyCode.S, KeyCode.UpArrow, KeyCode.W);
                    float s = InputState.pads[0] ? InputState.padAxes[1] : 0f;
                    return k != 0f ? k : (Mathf.Abs(s) > 0.19f ? s : 0f);
                }
                case "Mouse X": return (InputState.pointerX - InputState.pointerXBefore) * 0.1f;
                case "Mouse Y": return (InputState.pointerY - InputState.pointerYBefore) * 0.1f;
                case "Mouse ScrollWheel": return InputState.scrollY * 0.1f;
                default: return 0f;
            }
        }

        public static float GetAxis(string axisName) { return GetAxisRaw(axisName); }

        /// <summary>Unity's default buttons: Fire1 (left ctrl, mouse 0), Fire2 (left alt, mouse 1), Fire3 (left shift, mouse 2), Jump (space),
        /// Submit (return, space, pad A) and Cancel (escape, pad B).</summary>
        public static bool GetButton(string buttonName) { return Button(buttonName, false); }
        public static bool GetButtonDown(string buttonName) { return Button(buttonName, false) && !Button(buttonName, true); }
        public static bool GetButtonUp(string buttonName) { return !Button(buttonName, false) && Button(buttonName, true); }

        static bool Button(string buttonName, bool before)
        {
            switch (buttonName)
            {
                case "Fire1": return Held(KeyCode.LeftControl, before) || Held(KeyCode.Mouse0, before) || Held(KeyCode.JoystickButton0, before);
                case "Fire2": return Held(KeyCode.LeftAlt, before) || Held(KeyCode.Mouse1, before) || Held(KeyCode.JoystickButton1, before);
                case "Fire3": return Held(KeyCode.LeftShift, before) || Held(KeyCode.Mouse2, before) || Held(KeyCode.JoystickButton2, before);
                case "Jump": return Held(KeyCode.Space, before) || Held(KeyCode.JoystickButton3, before);
                case "Submit": return Held(KeyCode.Return, before) || Held(KeyCode.Space, before) || Held(KeyCode.JoystickButton0, before);
                case "Cancel": return Held(KeyCode.Escape, before) || Held(KeyCode.JoystickButton1, before);
                default: return false;
            }
        }
    }
}
