using System.Runtime.CompilerServices;

// Managed test target with the exact optional API shape. This does not stand in
// for the real paint/explosion behavior; the native fixture covers that.
namespace SkinManagerMod.Patches
{
    public static class TrainCarPaintPatches
    {
        public static int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ReapplyThemes(TrainCar car) { Calls++; }
    }
}
