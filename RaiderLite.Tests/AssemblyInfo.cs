// Tests share CoreSettings.Current, so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
