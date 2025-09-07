
using Xunit;
using FluentAssertions;

namespace XStateNet.UnitTest;
public static class Helper
{
    public static void AssertEquivalence(this string asIs, string toBe)
    {
        List<string> toBeList = new List<string>();
        
        toBeList = toBe.Split(';').ToList();

        asIs.Length.Should().Be(toBe.Length);

        foreach (var item in toBeList)
        {
            asIs.Should().Contain(item.Trim());
        }
    }
}
