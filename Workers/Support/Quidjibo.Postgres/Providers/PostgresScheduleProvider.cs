using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Models;
using Quidjibo.Providers;
using Quidjibo.Postgres.Extensions;
using Quidjibo.Postgres.Utils;
using Npgsql;

namespace Quidjibo.Postgres.Providers
{
    public class PostgresScheduleProvider : IScheduleProvider
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;
        private readonly string[] _queues;

        private string _receiveSql;

        public PostgresScheduleProvider(
            ILogger logger,
            string connectionString,
            string[] queues)
        {
            _logger = logger;
            _connectionString = connectionString;
            _queues = queues;
        }

        public async Task<List<ScheduleItem>> ReceiveAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var receiveOn = DateTime.UtcNow;

            if (_receiveSql == null)
            {
                _receiveSql = await SqlLoader.GetScript("Schedule.Receive");
                if (_queues.Length > 0)
                {
                    _receiveSql = _receiveSql.Replace("@Queue1",
                        string.Join(",", _queues.Select((x, i) => $"@Queue{i}")));
                }

				_receiveSql = _receiveSql.Replace("@ReceiveOn", $"'{receiveOn.ToString()}'");
				_receiveSql = _receiveSql.Replace("@VisibleOn", $"'{receiveOn.ToString()}'");
			}

            var items = new List<ScheduleItem>(100);
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = _receiveSql;
                cmd.AddParameter("@Take", 100);
                //cmd.AddParameter("@ReceiveOn", receiveOn);
                //cmd.AddParameter("@VisibleOn", receiveOn);

                // dynamic parameters
                _queues.Select((q, i) => cmd.Parameters.AddWithValue($"@Queue{i}", q)).ToList();
                using (var rdr = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await rdr.ReadAsync(cancellationToken))
                    {
                        var item = MapScheduleItem(rdr);
                        items.Add(item);
                    }
                }
            }, cancellationToken);
            return items;
        }

        public async Task CompleteAsync(ScheduleItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Schedule.Complete");
                cmd.AddParameter("@Id", item.Id);
                cmd.AddParameter("@EnqueueOn", item.EnqueueOn);
                cmd.AddParameter("@EnqueuedOn", item.EnqueuedOn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
        }

        public async Task CreateAsync(ScheduleItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Schedule.Create");
                cmd.AddParameter("@Id", item.Id);
                cmd.AddParameter("@Name", item.Name);
                cmd.AddParameter("@Queue", item.Queue);
                cmd.AddParameter("@CronExpression", item.CronExpression);
                cmd.AddParameter("@CreatedOn", item.CreatedOn);
                cmd.AddParameter("@EnqueuedOn", item.EnqueuedOn);
                cmd.AddParameter("@EnqueueOn", item.EnqueueOn);
                cmd.AddParameter("@VisibleOn", item.VisibleOn);
                cmd.AddParameter("@Payload", item.Payload);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
        }

        public async Task<ScheduleItem> LoadByNameAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            var item = default(ScheduleItem);
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Schedule.LoadByName");
                cmd.AddParameter("@Name", name);
                using (var rdr = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    if (await rdr.ReadAsync(cancellationToken))
                    {
                        item = MapScheduleItem(rdr);
                    }
                }
            }, cancellationToken);
            return item;
        }

        public Task UpdateAsync(ScheduleItem item, CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default(CancellationToken))
        {
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Schedule.Delete");
                cmd.AddParameter("@Id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
        }

        public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            var count = 0;
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Schedule.Exists");
                cmd.AddParameter("@Name", name);
                count = (int)await cmd.ExecuteScalarAsync(cancellationToken);
            }, cancellationToken);
            return count > 0;
        }

        private ScheduleItem MapScheduleItem(NpgsqlDataReader rdr)
        {
            return new ScheduleItem
            {
                CreatedOn = rdr.Map<DateTime>(nameof(ScheduleItem.CreatedOn).ToLowerInvariant()),
                CronExpression = rdr.Map<string>(nameof(ScheduleItem.CronExpression).ToLowerInvariant()),
                EnqueuedOn = rdr.Map<DateTime?>(nameof(ScheduleItem.EnqueuedOn).ToLowerInvariant()),
                EnqueueOn = rdr.Map<DateTime?>(nameof(ScheduleItem.EnqueueOn).ToLowerInvariant()),
                Id = rdr.Map<Guid>(nameof(ScheduleItem.Id).ToLowerInvariant()),
                Name = rdr.Map<string>(nameof(ScheduleItem.Name).ToLowerInvariant()),
                Payload = rdr.Map<byte[]>(nameof(ScheduleItem.Payload).ToLowerInvariant()),
                Queue = rdr.Map<string>(nameof(ScheduleItem.Queue).ToLowerInvariant())
            };
        }

        private Task ExecuteAsync(Func<NpgsqlCommand, Task> func, CancellationToken cancellationToken)
        {
            return PostgresRunner.ExecuteAsync(func, _connectionString, true, cancellationToken);
        }
    }
}
