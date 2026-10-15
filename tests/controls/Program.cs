using System;
using DVCCLControlFix;
class Program
{
    static int passed;
    static void Equal(string name, float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.00001f)
            throw new Exception(name + ": got " + actual + ", expected " + expected);
        passed++;
    }
    static void True(string name, bool ok)
    {
        if (!ok) throw new Exception(name + ": failed");
        passed++;
    }
    static void Main()
    {
        Equal("JNR 4-position train brake first apply", ConnectionMath.Move(0, 1, 4), 1f/3);
        Equal("JNR train brake release", ConnectionMath.Move(1, -1, 4), 2f/3);
        Equal("JNR 5-position independent brake first apply", ConnectionMath.Move(0, 1, 5), 0.25f);
        Equal("JNR independent brake release", ConnectionMath.Move(1, -1, 5), 0.75f);
        Equal("HUD multiple notches", ConnectionMath.Move(0, 3, 4), 1);
        Equal("Continuous reverser", ConnectionMath.Move(0.5f, 1, 0), 0.55f);
        Equal("Upper clamp held apply", ConnectionMath.Move(1, 8, 4), 1);
        Equal("Lower clamp held release", ConnectionMath.Move(0, -8, 5), 0);
        Equal("No movement without input", ConnectionMath.Move(0.3f, 0, 4), 0.3f);
        Equal("Two-position cab toggle step", ConnectionMath.Move(0, 1, 2), 1);
        Equal("Invalid notch count uses continuous step", ConnectionMath.Move(0.5f, 1, 1), 0.55f);
        float v = 0;
        for (int i = 0; i < 4; i++) v = ConnectionMath.Move(v, 1, 5);
        Equal("Repeated independent brake applies reach full", v, 1);
        for (int i = 0; i < 4; i++) v = ConnectionMath.Move(v, -1, 5);
        Equal("Repeated independent brake releases reach zero", v, 0);
        Console.WriteLine(passed + " stepping regression checks passed.");
        // 1.3.0 key plan
        var bound = new System.Collections.Generic.HashSet<string> { "throttle.EXT_IN", "cylinderCock.EXT_IN" };
        True("Leader cab No.2 regulator is a twin of cab No.1", KeyPlan.IsTwin("throttle.EXT_IN", false, bound));
        True("Leader cab No.2 cylinder cocks are a twin", KeyPlan.IsTwin("cylinderCock.EXT_IN", false, bound));
        True("Cab No.1 keeps its own existing input", !KeyPlan.IsTwin("throttle.EXT_IN", true, bound));
        True("Single-cab control on an unbound port still gets keys", !KeyPlan.IsTwin("blower.EXT_IN", false, bound));
        True("Control without a port (handbrake) is never a twin", !KeyPlan.IsTwin(null, false, bound));
        True("Bound on/off switch gets no axis", !KeyPlan.AddIncremental(true, true, true, false));
        True("On/off switch with no toggle bound gets the fix's full input", KeyPlan.AddIncremental(true, true, true, true));
        True("Lever missing its incremental still gets it (1.2.0 behaviour)", KeyPlan.AddIncremental(true, false, true, false));
        True("Switch without a DV toggle action keeps 1.2.0 behaviour", KeyPlan.AddIncremental(true, true, false, false));
        True("Nothing added when incremental is already bound", !KeyPlan.AddIncremental(false, true, true, false));
        Console.WriteLine(passed + " checks passed in total.");
    }
}
