using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Factories;
using Quidjibo.Providers;
using Quidjibo.Postgres.Providers;
using Quidjibo.Postgres.Utils;

namespace Quidjibo.Postgres.Factories
{
    public class PostgresProgressProviderFactory : IProgressProviderFactory
    {
        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);
        private readonly string _connectionString;

        private readonly ILoggerFactory _loggerFactory;
        private IProgressProvider _provider;

        public PostgresProgressProviderFactory(
            ILoggerFactory loggerFactory,
            string connectionString)
        {
            _loggerFactory = loggerFactory;
            _connectionString = connectionString;
        }

        public Task<IProgressProvider> CreateAsync(string queues, CancellationToken cancellationToken = default(CancellationToken))
        {
            return CreateAsync(queues.Split(','), cancellationToken);
        }

        public async Task<IProgressProvider> CreateAsync(string[] queues, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_provider != null)
            {
                return _provider;
            }

            try
            {
                await SyncLock.WaitAsync(cancellationToken);
                await PostgresRunner.ExecuteAsync(async cmd =>
                {
                    cmd.CommandText = await SqlLoader.GetScript("Progress.Setup");
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }, _connectionString, false, cancellationToken);

                _provider = new PostgresProgressProvider(
                    _loggerFactory.CreateLogger<PostgresProgressProvider>(),
                    _connectionString);
                return _provider;
            }
            finally
            {
                SyncLock.Release();
            }
        }
    }
}
