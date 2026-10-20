// CCSharp --unity-engine: the input a host reports each frame, for the Input System (unity-engine-managed/InputSystem.cs) and the old Input class to
// read.  In the subset, so a host can call its setters from C whatever else the program uses.
//
// Keys are indexed by Unity's Key enum (Key.A is 15).  Mouse buttons are bits: 1 left, 2 right, 4 middle, 8 back, 16 forward.  Up to four gamepads
// in GLFW's standard layout: buttons 0 A (south), 1 B (east), 2 X (west), 3 Y (north), 4 left shoulder, 5 right shoulder, 6 back (select), 7 start,
// 8 guide, 9 left stick, 10 right stick, 11 dpad up, 12 right, 13 down, 14 left; axes 0/1 the left stick, 2/3 the right (y up), 4/5 the triggers 0..1.

namespace UnityEngine;

public static class InputState
{
    public const int KeyCount = 112;
    public const int MaxPads = 4;
    public const int PadButtons = 15;
    public const int PadAxes = 6;

    public static bool keyboard;
    public static bool mouse;
    public static bool[] keys;
    public static bool[] keysBefore;
    public static float pointerX;
    public static float pointerY;
    public static float pointerXBefore;
    public static float pointerYBefore;
    public static int buttons;
    public static int buttonsBefore;
    public static float scrollX;
    public static float scrollY;
    public static bool[] pads;
    public static bool[] padButtons;
    public static bool[] padButtonsBefore;
    public static float[] padAxes;
    public static float[] padAxesBefore;

    static bool ready;

    /// <summary>Allocates the state, once (the subset has neither array field initialisers nor static constructors).  Every setter and the
    /// engine's Tick call it, so a reader never sees it missing.</summary>
    public static void Init()
    {
        if (ready) return;
        ready = true;
        keys = new bool[112];
        keysBefore = new bool[112];
        pads = new bool[4];
        padButtons = new bool[60];
        padButtonsBefore = new bool[60];
        padAxes = new float[24];
        padAxesBefore = new float[24];
    }

    public static void HostSetKeyboard(int present)
    {
        Init();
        keyboard = present != 0;
    }

    public static void HostSetKey(int key, int down)
    {
        Init();
        if (key > 0 && key < KeyCount) keys[key] = down != 0;
    }

    /// <summary>The pointer in window pixels from the bottom left, and the buttons held (bits).</summary>
    public static void HostSetPointer(float x, float y, int held)
    {
        Init();
        mouse = true;
        pointerX = x;
        pointerY = y;
        buttons = held;
    }

    /// <summary>Wheel movement since the last frame (notches; y > 0 away from the user).</summary>
    public static void HostAddScroll(float x, float y)
    {
        Init();
        scrollX = scrollX + x;
        scrollY = scrollY + y;
    }

    public static void HostSetPad(int pad, int connected)
    {
        Init();
        if (pad >= 0 && pad < MaxPads) pads[pad] = connected != 0;
    }

    public static void HostSetPadButton(int pad, int button, int down)
    {
        Init();
        if (pad >= 0 && pad < MaxPads && button >= 0 && button < PadButtons) padButtons[pad * PadButtons + button] = down != 0;
    }

    public static void HostSetPadAxis(int pad, int axis, float value)
    {
        Init();
        if (pad >= 0 && pad < MaxPads && axis >= 0 && axis < PadAxes) padAxes[pad * PadAxes + axis] = value;
    }

    /// <summary>The end of a frame: what is held now is what was held before the next one (the engine calls it).</summary>
    public static void EndFrame()
    {
        Init();
        for (int i = 0; i < KeyCount; i++) keysBefore[i] = keys[i];
        for (int i = 0; i < MaxPads * PadButtons; i++) padButtonsBefore[i] = padButtons[i];
        for (int i = 0; i < MaxPads * PadAxes; i++) padAxesBefore[i] = padAxes[i];
        buttonsBefore = buttons;
        pointerXBefore = pointerX;
        pointerYBefore = pointerY;
        scrollX = 0f;
        scrollY = 0f;
    }
}
