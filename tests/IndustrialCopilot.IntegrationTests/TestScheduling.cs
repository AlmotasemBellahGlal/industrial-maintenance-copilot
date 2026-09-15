// Each fixture creates/drops a real database. Serial fixture scheduling keeps a local
// Docker/WSL disk from being flooded by concurrent forced checkpoints. Concurrency tests
// still exercise simultaneous operations explicitly inside their test methods.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
