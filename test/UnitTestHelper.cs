using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
namespace SharpState.UnitTest;
public static class Helper
{
    public static void AssertEquivalence(this string asIs, string toBe)
    {
        List<string> toBeList = new List<string>();
        
        toBeList = toBe.Split(';').ToList();

        Assert.AreEqual(asIs.Length, toBe.Length);

        foreach (var item in toBeList)
        {
            Assert.IsTrue(asIs.Contains(item.Trim()));
        }
    }
}
