namespace XStateNet.GPU.Tests
{
    public enum TestPriority
    {
        Critical = 0,
        High = 1,
        Normal = 2,
        Low = 3
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TestPriorityAttribute : Attribute
    {
        public TestPriority Priority { get; }

        public TestPriorityAttribute(TestPriority priority)
        {
            Priority = priority;
        }
    }
}