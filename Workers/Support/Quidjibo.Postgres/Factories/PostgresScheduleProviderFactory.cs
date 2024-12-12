using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Factories;
using Quidjibo.Providers;
using Quidjibo.Postgres.Providers;
using Quidjibo.Postgres.Utils;

namespace Quidjibo.Postgres.Factories
{
    public class PostgresScheduleProviderFactory : IScheduleProviderFactory
    {
        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);
        private readonly string _connectionString;

        private readonly ILoggerFactory _loggerFactory;

        public PostgresScheduleProviderFactory(
            ILoggerFactory loggerFactory,
            string connectionString)
        {
            _loggerFactory = loggerFactory;
            _connectionString = connectionString;
        }

        public async Task<IScheduleProvider> CreateAsync(string queues, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await CreateAsync(queues.Split(','), cancellationToken);
        }

        public async Task<IScheduleProvider> CreateAsync(string[] queues, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                await SyncLock.WaitAsync(cancellationToken);
                await PostgresRunner.ExecuteAsync(async cmd =>
                {
                    var schemaSetup = await SqlLoader.GetScript("Schema.Setup");
                    var scheduleSetup = await SqlLoader.GetScript("Schedule.Setup");
                    cmd.CommandText = $"{schemaSetup};\r\n{scheduleSetup}";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }, _connectionString, false, cancellationToken);

                return await Task.FromResult<IScheduleProvider>(new PostgresScheduleProvider(
                    _loggerFactory.CreateLogger<PostgresScheduleProvider>(),
                    _connectionString, queues));
            }
            finally
            {
                SyncLock.Release();
            }
        }
    }
}
