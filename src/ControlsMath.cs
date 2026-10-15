using System;
namespace DVCCLControlFix;
// Kept free of Unity types so the exact stepping calculation can be tested.
public static class ConnectionMath
{
    public static float Move(float value, int notches, int positions)
    {
        float step = positions > 1 ? 1f / (positions - 1) : 0.05f;
        return Math.Max(0f, Math.Min(1f, value + notches * step));
    }
}

// 1.3.0 key-binding decisions, kept free of Unity types for the same reason.
public static class KeyPlan
{
    // A control is a twin when another control on the same simulation port already has keyboard input.
    public static bool IsTwin(string port, bool hasOwnInput, System.Collections.Generic.ICollection<string> portsWithInput)
        => !string.IsNullOrEmpty(port) && !hasOwnInput && portsWithInput.Contains(port);

    // Whether to add the incremental (axis) action: never to an on/off switch whose toggle action is already bound.
    public static bool AddIncremental(bool missingIncremental, bool isSwitch, bool toggleExists, bool missingToggle)
        => missingIncremental && !(isSwitch && toggleExists && !missingToggle);
}
