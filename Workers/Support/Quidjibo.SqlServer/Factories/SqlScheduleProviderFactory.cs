using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Factories;
using Quidjibo.Providers;
using Quidjibo.SqlServer.Providers;
using Quidjibo.SqlServer.Utils;

namespace Quidjibo.SqlServer.Factories
{
    public class SqlScheduleProviderFactory : IScheduleProviderFactory
    {
        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);
        private readonly string _connectionString;

        private readonly ILoggerFactory _loggerFactory;

        public SqlScheduleProviderFactory(
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
                await SqlRunner.ExecuteAsync(async cmd =>
                {
                    var schemaSetup = await SqlLoader.GetScript("Schema.Setup");
                    var scheduleSetup = await SqlLoader.GetScript("Schedule.Setup");
                    cmd.CommandText = $"{schemaSetup};\r\n{scheduleSetup}";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }, _connectionString, false, cancellationToken);

                return await Task.FromResult<IScheduleProvider>(new SqlScheduleProvider(
                    _loggerFactory.CreateLogger<SqlScheduleProvider>(),
                    _connectionString, queues));
            }
            finally
            {
                SyncLock.Release();
            }
        }
    }
}