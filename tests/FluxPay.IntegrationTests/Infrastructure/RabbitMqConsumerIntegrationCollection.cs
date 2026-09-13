namespace FluxPay.IntegrationTests.Infrastructure;

[CollectionDefinition(
    Name,
    DisableParallelization = true)]
public sealed class RabbitMqConsumerIntegrationCollection
    : ICollectionFixture<RabbitMqConsumerIntegrationFixture>
{
    public const string Name =
        "RabbitMQ consumer integration";
}
