using UnityEngine;

// Skin Manager's real SendMessage calls need receivers in this tiny fixture.
public sealed class FixtureSkinMessages : MonoBehaviour
{
    public int BeforeCount, AfterCount;
    public void BeforeSkinChange() { BeforeCount++; }
    public void AfterSkinChange() { AfterCount++; }
}
