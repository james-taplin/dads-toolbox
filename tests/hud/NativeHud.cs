using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityModManagerNet;

internal static class NativeHud
{
    internal static string GameRoot;
    internal static Assembly Hud, Game;
    internal static List<MethodInfo> Providers;
    internal static object UnsupportedCar, ValidCar;
    internal static int ThrowMode=0;
    internal static void ResolveFrom(string root)
    {
        GameRoot = root;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string[] dirs = { Path.Combine(GameRoot,"DerailValley_Data","Managed"),
            Path.Combine(GameRoot,"DerailValley_Data","Managed","UnityModManager"),
            Path.Combine(GameRoot,"Mods","HeadsUpDisplay_994"), AppDomain.CurrentDomain.BaseDirectory };
        foreach (string dir in dirs)
        {
            string file = Path.Combine(dir, name);
            if (File.Exists(file)) return Assembly.LoadFrom(file);
        }
        return null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Load()
    {
        Hud = Assembly.LoadFrom(Path.Combine(GameRoot,"Mods","HeadsUpDisplay_994","HeadsUpDisplay.dll"));
        Game = Assembly.LoadFrom(Path.Combine(GameRoot,"DerailValley_Data","Managed","Assembly-CSharp.dll"));
        Providers = new List<MethodInfo>();
        foreach (Type nested in Hud.GetType("DvMod.HeadsUpDisplay.LocoProviders",true).GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))
            foreach (MethodInfo m in nested.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
                if (m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == Game.GetType("TrainCar")) Providers.Add(m);
        UnsupportedCar = Car(false);
        ValidCar = Car(true);
    }
    internal static FieldInfo Field(Type type, string name)
    { return type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static); }
    internal static object Empty(Type type) { return FormatterServices.GetUninitializedObject(type); }
    private static object Car(bool simulation)
    {
        Type type = Game.GetType("TrainCar",true);
        object car = Empty(type);
        FieldInfo cached = Field(type,"_simController");
        Type simType = cached.FieldType.GetGenericArguments()[0];
        object sim = simulation ? Empty(simType) : null;
        cached.SetValue(car,Activator.CreateInstance(cached.FieldType,new object[]{sim,true}));
        if (sim != null)
        {
            FieldInfo field = Field(simType,"drivingForce");
            object force = Empty(field.FieldType);
            Field(field.FieldType,"generatedForce").SetValue(force,125000f);
            field.SetValue(sim,force);
            field = Field(simType,"wheelslipController");
            object slip = Empty(field.FieldType);
            field.FieldType.GetProperty("wheelslip").GetSetMethod(true).Invoke(slip,new object[]{.25f});
            field.SetValue(sim,slip);
        }
        return car;
    }
    internal static object Invoke(MethodInfo method, object car)
    {
        try { return method.Invoke(method.IsStatic ? null : Empty(method.DeclaringType),new object[]{car}); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    internal static object Query(MethodInfo method)
    {
        Type type = Hud.GetType(method.ReturnType == typeof(float?) ? "DvMod.HeadsUpDisplay.FloatQueryDataProvider" : "DvMod.HeadsUpDisplay.QuantityQueryDataProvider`1",true);
        if (type.IsGenericTypeDefinition) type = type.MakeGenericType(method.ReturnType.GetGenericArguments()[0]);
        ConstructorInfo ctor = type.GetConstructors()[0];
        ParameterInfo[] p = ctor.GetParameters();
        Delegate reader = method.IsStatic ? Delegate.CreateDelegate(p[1].ParameterType,method) :
            Delegate.CreateDelegate(p[1].ParameterType,Empty(method.DeclaringType),method);
        if (method.ReturnType == typeof(float?))
            return ctor.Invoke(new object[]{"test",reader,Delegate.CreateDelegate(p[2].ParameterType,typeof(NativeHud).GetMethod("Format")),null,false});
        return ctor.Invoke(new object[]{"test",reader,null,false});
    }
    public static string Format(float value) { return value.ToString("P1"); }
    internal static MethodInfo SpeedMethod=null;
    internal static int SpeedReplacements;
    public static float FixedSpeed(object car) { return 36f; }
    // Replace only the engine speed call in the real native Power lambda.
    // Patching TrainCar.GetForwardSpeed itself tries to prepare Unity ECalls.
    public static IEnumerable<CodeInstruction> SpeedCode(IEnumerable<CodeInstruction> original)
    {
        foreach(CodeInstruction instruction in original)
        {
            if(Object.Equals(instruction.operand,SpeedMethod))
            {
                instruction.opcode=OpCodes.Call;
                instruction.operand=typeof(NativeHud).GetMethod("FixedSpeed");
                SpeedReplacements++;
            }
            yield return instruction;
        }
    }
    public static void Inject()
    {
        if (ThrowMode == 1) throw new NullReferenceException("injected supported-car NRE");
        if (ThrowMode == 2) throw new InvalidOperationException("unrelated error");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Sentinel() { return 4; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void VoidTarget() { }
    public static void SentinelPostfix(ref int __result) { __result += 10; }
    internal static bool Has(MethodBase method,string owner)
    { Patches info=Harmony.GetPatchInfo(method);return info!=null && info.Owners.Contains(owner); }
    internal static int Count(MethodBase method,string owner)
    {
        Patches info=Harmony.GetPatchInfo(method);int n=0;
        if(info!=null)foreach(Patch p in info.Finalizers)if(p.owner==owner)n++;
        return n;
    }
    internal static UnityModManager.ModEntry Entry(string id,Assembly assembly,bool active)
    {
        var entry=(UnityModManager.ModEntry)Empty(typeof(UnityModManager.ModEntry));
        Field(entry.GetType(),"Info").SetValue(entry,new UnityModManager.ModInfo{Id=id,AssemblyName="Optional.dll"});
        Field(entry.GetType(),"mAssembly").SetValue(entry,assembly);
        Field(entry.GetType(),"mActive").SetValue(entry,active);
        entry.Enabled=true;
        return entry;
    }
}
