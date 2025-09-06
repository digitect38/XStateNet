
namespace XStateNet.UnitTest;
public static class Helper
{
    public static void AssertEquivalence(this string asIs, string toBe)
    {
        List<string> toBeList = new List<string>();
        
        toBeList = toBe.Split(';').ToList();

        Assert.That(asIs.Length, Is.EqualTo(toBe.Length));

        foreach (var item in toBeList)
        {
            Assert.That(asIs.Contains(item.Trim()), Is.True);
        }
    }
}
