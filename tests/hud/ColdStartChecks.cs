using System;
using System.Runtime.CompilerServices;
using DadsToolbox.HudGuard;
using UnityModManagerNet;
public static class ColdStartChecks
{
    public static int Main(string[] args)
    {
        NativeHud.ResolveFrom(args[0]);
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Run()
    {
        UnityModManager.modEntries.Clear();
        var module=new HudGuardModule(null);
        module.Configure(true,true);module.Tick();
        bool absent=module.State==HudGuardState.WaitingForHud && module.DesiredEnabled && module.PatchedProviderCount==0;
        bool noOptional=true;
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name=assembly.GetName().Name;
            if(name=="HeadsUpDisplay" || name=="QuantitiesNet" || name=="Assembly-CSharp" || name.StartsWith("CCL."))noOptional=false;
        }
        module.Configure(false,true);bool disabled=module.State==HudGuardState.Disabled;
        bool cleanup=module.Unload();
        Console.WriteLine((absent?"PASS ":"FAIL ")+"cold startup waits safely for absent HUD");
        Console.WriteLine((noOptional?"PASS ":"FAIL ")+"HUD, quantities, game and CCL assemblies remain unloaded");
        Console.WriteLine((disabled?"PASS ":"FAIL ")+"option can disable without optional dependencies");
        Console.WriteLine((cleanup?"PASS ":"FAIL ")+"module unloads without optional dependencies");
        Console.WriteLine("RESULT cold-start="+(absent&&noOptional&&disabled&&cleanup?"4 passed, 0 failed":"FAILED"));
        return absent&&noOptional&&disabled&&cleanup?0:1;
    }
}
