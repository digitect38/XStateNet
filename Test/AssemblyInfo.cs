using System.Diagnostics.CodeAnalysis;
using Xunit;

// Configure test collection ordering to run timing-sensitive tests first
[assembly: TestCollectionOrderer("XStateNet.Tests.TestInfrastructure.PriorityCollectionOrderer", "XStateNet.Tests")]

// Configure test case ordering within collections
[assembly: TestCaseOrderer("XStateNet.Tests.TestInfrastructure.PriorityOrderer", "XStateNet.Tests")]

// Configure parallel execution with controlled concurrency
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = false, MaxParallelThreads = 4)]

// Suppress certain code analysis warnings for test projects
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Test methods cannot be static")]
[assembly: SuppressMessage("Usage", "xUnit1013:Public method should be marked as test", Justification = "Helper methods are allowed")]