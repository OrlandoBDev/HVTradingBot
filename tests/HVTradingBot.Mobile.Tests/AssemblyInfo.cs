// The engine supervisor reads the process-wide Environment.ExitCode (the worker's restart request), and the runtime
// tests start real engines, so the tests in this assembly run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
