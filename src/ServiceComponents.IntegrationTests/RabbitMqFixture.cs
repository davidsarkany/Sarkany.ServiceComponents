using System;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace ServiceComponents.IntegrationTests
{
    /// <summary>
    /// One RabbitMQ broker per test collection.
    /// </summary>
    public class RabbitMqFixture : IAsyncLifetime
    {
        public const string Image = "rabbitmq:4.1-management";

        private readonly RabbitMqContainer _container = new RabbitMqBuilder().WithImage(Image).Build();

        public string ConnectionString => _container.GetConnectionString();

        public Task InitializeAsync() => _container.StartAsync();

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();

        public async Task<uint> MessageCount(string queue)
        {
            await using var connection = await new ConnectionFactory { Uri = new Uri(ConnectionString) }.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
        }

        public async Task<bool> Exists(string queue)
        {
            await using var connection = await new ConnectionFactory { Uri = new Uri(ConnectionString) }.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            try {
                await channel.QueueDeclarePassiveAsync(queue);
                return true;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) {
                return false;
            }
        }
    }

    [CollectionDefinition("rabbitmq")]
    public class RabbitMqCollection : ICollectionFixture<RabbitMqFixture> { }
}
