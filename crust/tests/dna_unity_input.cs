// dna
// unity-engine
// The Input System over the host's input state (unity-engine/Input.cs), read where a game reads it, in Update: a key held for two frames is
// pressed, held, then released; onEvent runs before the frame's scripts, seeing the last frame's value through isPressed and the new one through
// ReadValueFromEvent; the old Input class; a gamepad's buttons and stick; the mouse's position and buttons.
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

public class Watcher : MonoBehaviour
{
    public static string log = "";

    public override void Update()
    {
        KeyControl a = Keyboard.current.aKey;
        log += "[" + (a.wasPressedThisFrame ? "P" : "") + (a.isPressed ? "H" : "") + (a.wasReleasedThisFrame ? "R" : "")
            + (Input.GetKeyDown(KeyCode.A) ? "d" : "") + (Input.GetKeyUp(KeyCode.A) ? "u" : "");
        Gamepad pad = Gamepad.current;
        if (pad != null)
            log += " pad " + Gamepad.all.Count + (pad.buttonSouth.wasPressedThisFrame ? " south" : "") + (pad.leftStick.left.isPressed ? " left" : "")
                + " " + (int)pad.leftStick.ReadValue().x;
        Mouse m = Mouse.current;
        if (m != null)
            log += " mouse " + (int)m.position.ReadValue().y + (m.rightButton.wasPressedThisFrame ? " right" : "") + (m.leftButton.isPressed ? " left" : "");
        log += "]";
    }
}

class Program
{
    static string events = "";

    static void OnEvent(InputEventPtr e, InputDevice device)
    {
        if (!e.IsA<StateEvent>()) return;
        foreach (InputControl c in e.EnumerateChangedControls(device))
        {
            ButtonControl b = c as ButtonControl;
            if (b != null) events += b.name + (b.isPressed ? "1" : "0") + (b.IsValueConsideredPressed(b.ReadValueFromEvent(e)) ? "1" : "0") + " ";
        }
    }

    static int Main()
    {
        GameObject g = new GameObject("Watcher");
        g.AddComponent<Watcher>();
        InputSystem.onEvent += OnEvent;
        Time.deltaTime = 0.02f;
        InputState.HostSetKeyboard(1);
        Engine.Tick();
        InputState.HostSetKey((int)Key.A, 1);
        Engine.Tick();
        Engine.Tick();
        InputState.HostSetKey((int)Key.A, 0);
        Engine.Tick();
        InputState.HostSetPad(0, 1);
        InputState.HostSetPadButton(0, 0, 1);
        InputState.HostSetPadAxis(0, 0, -1f);
        Engine.Tick();
        InputState.HostSetPointer(10f, 20f, 2);
        Engine.Tick();
        Console.WriteLine(Watcher.log);     // [][PHd][H][Ru][ pad 1 south left -1][ pad 1 left -1 mouse 20 right]
        Console.WriteLine(events);          // a01 anyKey01 a10 anyKey10 buttonSouth01 left01 rightButton01 press01
        return 0;
    }
}
