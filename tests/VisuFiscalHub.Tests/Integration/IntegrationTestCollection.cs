using Xunit;

namespace VisuFiscalHub.Tests.Integration;

// DisableParallelization prevents concurrent factory constructors from racing on
// Environment.SetEnvironmentVariable (process-wide state) during WebApplicationFactory startup.
[CollectionDefinition("IntegrationTests", DisableParallelization = true)]
public sealed class IntegrationTestCollection { }
