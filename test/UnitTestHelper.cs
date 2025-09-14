
using Xunit;


namespace XStateNet.UnitTest;
public static class Helper
{
    public static void AssertEquivalence(this string asIs, string toBe)
    {
        List<string> toBeList = new List<string>();

        toBeList = toBe.Split(';').ToList();

        Assert.Equal(toBe.Length, asIs.Length);

        foreach (var item in toBeList)
        {
            Assert.Contains(item.Trim(), asIs);
        }
    }
}
