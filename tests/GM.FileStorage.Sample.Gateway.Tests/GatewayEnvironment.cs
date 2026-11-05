using Xunit;

namespace GM.FileStorage.Sample.Gateway.Tests;

/// <summary>
/// The gateway tests configure the gateway through environment variables (it reads its settings before a test's
/// overrides would apply), which the whole process shares, so they run one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GatewayEnvironment
{
    public const string Name = "gateway";
}
