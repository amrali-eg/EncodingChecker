using Xunit;

// DetectionCountTests measures process-global counters to assert that EC never works out
// a file's encoding twice. Those counts are only meaningful if nothing else is detecting
// at the same time, and xUnit runs test classes in parallel by default.
//
// The whole suite runs in well under a second, so serialising it costs nothing worth
// weighing against being able to state that invariant as a test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
