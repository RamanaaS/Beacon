namespace Beacon.IntegrationTests;

/// <summary>
/// Every integration test class shares exactly one BeaconApiFactory instance via this collection,
/// instead of each declaring its own IClassFixture&lt;BeaconApiFactory&gt;. That distinction
/// matters: IClassFixture gives each test *class* its own instance, and xUnit runs different
/// classes in parallel by default — so two classes each resetting the same physical
/// "BeaconDb_IntegrationTests" database concurrently is a real race (one class's EnsureDeleted
/// dropping the database out from under the other's Migrate/tests). ICollectionFixture forces
/// every class in the collection to share one instance and run sequentially against it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<BeaconApiFactory>
{
    public const string Name = "Integration";
}
