// dna
// unity-engine
// UnityEngine's UI with Unity's numbers: a stretched child follows its parent's rectangle, offsetMin and SetInsetAndSizeFromParentEdge move the
// edges as Unity's do, world corners take the parent's scale; a slider clamps and rounds, and lays out its fill and handle; a toggle group keeps
// one toggle on; a UnityEvent calls its persistent call (by name) then its listener; Select goes through the EventSystem.
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Counter : MonoBehaviour
{
    public int clicks;
    public void Click() { clicks += 1; }
    public void Add(int n) { clicks += n; }
}

public class Watch : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    public string log = "";
    public void OnSelect(BaseEventData d) { log += "+" + name; }
    public void OnDeselect(BaseEventData d) { log += "-" + name; }
}

class Program
{
    static string V(Vector2 v) { return (int)Math.Round(v.x) + "," + (int)Math.Round(v.y); }

    static RectTransform Rect(string name, RectTransform parent)
    {
        GameObject g = new GameObject(name);
        RectTransform r = g.MakeRect();
        if (parent != null) r.SetParent(parent, false);
        return r;
    }

    static int Main()
    {
        Time.deltaTime = 0.02f;
        RectTransform root = Rect("Root", null);
        root.sizeDelta = new Vector2(200f, 100f);
        RectTransform child = Rect("Child", root);
        child.anchorMin = Vector2.zero;
        child.anchorMax = Vector2.one;
        child.sizeDelta = new Vector2(-20f, -10f);                  // 10 and 5 in from each edge
        Console.WriteLine(V(child.rect.size) + " " + V(child.anchoredPosition));      // 180,90 0,0
        root.sizeDelta = new Vector2(400f, 200f);
        Console.WriteLine(V(child.rect.size));                                         // 380,190
        child.offsetMin = new Vector2(30f, 20f);
        Console.WriteLine(V(child.offsetMin) + " " + V(child.offsetMax) + " " + V(child.rect.size));   // 30,20 -10,-5 360,175
        RectTransform side = Rect("Side", root);
        side.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 15f, 50f);
        Console.WriteLine(V(side.anchorMin) + " " + V(side.anchoredPosition) + " " + (int)side.rect.width);   // 1,0 -40,0 50 (only x is anchored to the right edge: y stays 0.5, which rounds to 0)
        root.localScale = new Vector3(2f, 2f, 1f);
        Vector3[] corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Console.WriteLine(V(corners[0]) + " " + V(corners[2]));                                          // -340,-160 380,190

        GameObject sg = new GameObject("Slider");
        sg.MakeRect();
        Slider slider = sg.AddComponent<Slider>();
        RectTransform fill = Rect("Fill", sg.transform as RectTransform);
        RectTransform handle = Rect("Handle", sg.transform as RectTransform);
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.minValue = 0f;
        slider.maxValue = 10f;
        slider.wholeNumbers = true;
        float seen = -1f;
        slider.onValueChanged.AddListener(v => seen = v);
        slider.value = 3.6f;
        Console.WriteLine((int)slider.value + " " + (int)seen + " " + (int)(fill.anchorMax.x * 10f) + " " + (int)(handle.anchorMin.x * 10f));   // 4 4 4 4
        slider.value = 99f;
        Console.WriteLine((int)slider.value + " " + (int)(slider.normalizedValue * 100f));                // 10 100

        GameObject groupObject = new GameObject("Group");
        ToggleGroup group = groupObject.AddComponent<ToggleGroup>();
        Toggle a = new GameObject("A").AddComponent<Toggle>();
        Toggle b = new GameObject("B").AddComponent<Toggle>();
        Engine.Tick();                                              // OnEnable registers them
        a.group = group;
        b.group = group;
        a.isOn = true;
        b.isOn = true;
        Console.WriteLine((a.isOn ? 1 : 0) + " " + (b.isOn ? 1 : 0));                                   // 0 1
        b.isOn = false;
        Console.WriteLine((b.isOn ? 1 : 0) + " " + (group.AnyTogglesOn() ? 1 : 0));                     // 1 1 (a group without switch-off keeps one on)

        Counter counter = new GameObject("Counter").AddComponent<Counter>();
        Button button = new GameObject("Button").AddComponent<Button>();
        button.onClick.AddPersistentCall(counter, "Click", PersistentListenerMode.Void, null);
        button.onClick.AddPersistentCall(counter, "Add", PersistentListenerMode.Int, 10);
        button.onClick.AddListener(() => counter.clicks *= 2);
        button.onClick.Invoke();
        Console.WriteLine(counter.clicks);                                                                   // 22

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        Watch w1 = new GameObject("W1").AddComponent<Watch>();
        Selectable s1 = w1.gameObject.AddComponent<Selectable>();
        Watch w2 = new GameObject("W2").AddComponent<Watch>();
        Selectable s2 = w2.gameObject.AddComponent<Selectable>();
        Engine.Tick();
        s1.Select();
        s2.Select();
        Console.WriteLine(w1.log + " " + w2.log + " " + EventSystem.current.currentSelectedGameObject.name);   // +W1-W1 +W2 W2
        return 0;
    }
}
