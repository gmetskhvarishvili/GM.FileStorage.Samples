using Xunit;

// The end-to-end tests share one Postgres database (and each boots its own host, which migrates it on startup).
// Run the assembly serially so they never race over that shared state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
