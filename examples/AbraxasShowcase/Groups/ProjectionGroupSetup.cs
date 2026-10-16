using AbraxasDB;

namespace AbraxasShowcase;

/// <summary>Extends the customer/order environment with one deterministic graph for progressive PropPath navigation.<br/></summary>
internal static class ProjectionGroupSetup
{
    /// <summary>Creates the ordinary customer/order stores and one additional store for inherited-collection path examples.<br/></summary>
    /// <param name="directory">Showcase-owned RecordBase directory.<br/></param>
    internal static void CreateEnvironment(string directory)
    {
        RecordBaseFoundationsGroupSetup.CreateEnvironment(directory);
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        recordBase.CreateOrOpenStore<ShowcasePathGraph>("path-graphs", "PropPath navigation through stored object graphs");
    }

    /// <summary>Generates the standard customer/order baseline and one shared-reference graph before scenarios run.<br/></summary>
    /// <param name="directory">Showcase-owned RecordBase directory.<br/></param>
    /// <param name="request">User-selected deterministic baseline size and seed.<br/></param>
    /// <param name="progress">Optional generation progress callback.<br/></param>
    /// <param name="token">Cancellation observed before writing the graph.<br/></param>
    internal static void GenerateData(string directory, GenerationRequest request, Action<int, int, string>? progress, CancellationToken token)
    {
        RecordBaseFoundationsGroupSetup.GenerateDeterministicData(directory, request.CustomerCount, request.OrdersPerCustomer, request.Seed, progress, token);
        token.ThrowIfCancellationRequested();
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        recordBase.OpenStore<ShowcasePathGraph>("path-graphs").Put(ShowcasePathGraph.CreateExample());
    }
}
