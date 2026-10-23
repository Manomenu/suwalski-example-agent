// The agent's ActivitySource is process-wide: tracer providers of tests running at the same time all tag the same spans
// (one test's release landed on another's turn). The tests are short, so they run one after the other.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
