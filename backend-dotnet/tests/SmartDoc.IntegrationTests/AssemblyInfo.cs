using Xunit;

// All integration tests share the same real Postgres instance (docker-compose.yml) with no
// per-test isolation (no transaction rollback, no separate schema). Running test classes in
// parallel (xUnit's default) risks cross-test interference — e.g. a job-processing test
// picking up a ProcessingJob another test class hasn't cleaned up yet. Sequential execution
// trades some speed for correctness, which is the right call for a suite this size.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
