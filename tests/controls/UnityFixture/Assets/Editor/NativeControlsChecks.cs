using System;
using System.IO;
using System.Reflection;
using UnityEditor;

// The compiled fixture loads exact production/native assemblies by reflection.
// This Editor entrypoint only supplies Unity's actual Mono/ECall runtime.
public static class NativeControlsChecks
{
    public static void Run()
    {
        string game=Environment.GetEnvironmentVariable("TOOLBOX_GAME_PATH");
        string toolbox=Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_DLL");
        string fixture=Environment.GetEnvironmentVariable("TOOLBOX_CONTROLS_FIXTURE");
        string output=Environment.GetEnvironmentVariable("TOOLBOX_FIXTURE_OUT");
        int exit=2;
        AppDomain.CurrentDomain.AssemblyResolve += (sender,args)=> {
            string name=new AssemblyName(args.Name).Name;
            if(name==AssemblyName.GetAssemblyName(toolbox).Name)return Assembly.LoadFrom(toolbox);
            foreach(string root in new[]{Path.Combine(game,"DerailValley_Data","Managed"),Path.Combine(game,"DerailValley_Data","Managed","UnityModManager")})
            {string path=Path.Combine(root,name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}
            return null;
        };
        try
        {
            var assembly=Assembly.LoadFrom(fixture);
            var main=assembly.GetType("LifecycleChecks",true).GetMethod("Main",BindingFlags.Public|BindingFlags.Static);
            exit=(int)main.Invoke(null,new object[]{new[]{Path.Combine(game,"DerailValley_Data","Managed"),toolbox,output}});
        }
        catch(Exception ex){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"native-runner-error.txt"),ex.ToString());UnityEngine.Debug.LogException(ex);}
        EditorApplication.Exit(exit);
    }
}
