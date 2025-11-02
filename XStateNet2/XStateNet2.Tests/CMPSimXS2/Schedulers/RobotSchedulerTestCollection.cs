using Xunit;

namespace XStateNet2.Tests.CMPSimXS2.Schedulers;

/// <summary>
/// Collection definition for Robot Scheduler tests.
/// Tests in this collection run sequentially to avoid Akka.NET actor system timing issues.
/// </summary>
[CollectionDefinition("RobotScheduler", DisableParallelization = true)]
public class RobotSchedulerTestCollection
{
    // This class is never instantiated. It exists only to define the collection.
}
