namespace FluxPay.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string DefaultExchangeName =
        "fluxpay.events";

    public string HostName { get; init; } =
        string.Empty;

    public int Port { get; init; } =
        5672;

    public string UserName { get; init; } =
        string.Empty;

    public string Password { get; init; } =
        string.Empty;

    public string VirtualHost { get; init; } =
        string.Empty;

    public string ExchangeName { get; init; } =
        DefaultExchangeName;

    public string ClientProvidedName { get; init; } =
        "fluxpay-outbox-publisher";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HostName))
        {
            throw new ArgumentException(
                "RabbitMQ hostname is required.",
                nameof(HostName));
        }

        if (
            Port <= 0
            || Port > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Port),
                Port,
                "RabbitMQ port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            throw new ArgumentException(
                "RabbitMQ username is required.",
                nameof(UserName));
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new ArgumentException(
                "RabbitMQ password is required.",
                nameof(Password));
        }

        if (string.IsNullOrWhiteSpace(VirtualHost))
        {
            throw new ArgumentException(
                "RabbitMQ virtual host is required.",
                nameof(VirtualHost));
        }

        if (string.IsNullOrWhiteSpace(ExchangeName))
        {
            throw new ArgumentException(
                "RabbitMQ exchange name is required.",
                nameof(ExchangeName));
        }

        if (string.IsNullOrWhiteSpace(ClientProvidedName))
        {
            throw new ArgumentException(
                "RabbitMQ client-provided name is required.",
                nameof(ClientProvidedName));
        }
    }
}
