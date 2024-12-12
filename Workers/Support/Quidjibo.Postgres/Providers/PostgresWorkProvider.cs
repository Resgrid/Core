// // Copyright (c) smiggleworth. All rights reserved.
// // Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
    public class PostgresWorkProvider : IWorkProvider
    {
        public enum StatusFlags
        {
            Faulted = -1,
            New = 0,
            InFlight = 1,
            Complete = 2
        }

        public const int DEFAULT_EXPIRE_DAYS = 7;

        private readonly int _batchSize;
        private readonly string _connectionString;
        private readonly int _daysToKeep;
        private readonly ILogger _logger;
        private readonly int _maxAttempts;
        private readonly string[] _queues;
        private readonly int _visibilityTimeout;
        private string _receiveSql;

        public PostgresWorkProvider(
            ILogger logger,
            string connectionString,
            string[] queues,
            int visibilityTimeout,
            int batchSize,
            int daysToKeep
        )
        {
            _logger = logger;
            _queues = queues;
            _visibilityTimeout = visibilityTimeout;
            _batchSize = batchSize;
            _maxAttempts = 10;
            _connectionString = connectionString;
            _daysToKeep = Math.Abs(daysToKeep);
        }

        public async Task SendAsync(WorkItem item, int delay, CancellationToken cancellationToken)
        {
            await ExecuteAsync(async cmd =>
            {
                await cmd.PrepareForSendAsync(item, delay, cancellationToken);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<WorkItem>> ReceiveAsync(string worker, CancellationToken cancellationToken)
        {
            var receiveOn = DateTime.UtcNow;
            var deleteOn = _daysToKeep > 0 ? receiveOn.AddDays(-_daysToKeep) : receiveOn.AddHours(-1);

            if (_receiveSql == null)
            {
                _receiveSql = await SqlLoader.GetScript("Work.Receive");
                if (_queues.Length > 0)
                {
                    _receiveSql = _receiveSql.Replace("@Queue1",
                        string.Join(",", _queues.Select((x, i) => $"@Queue{i}")));
                }
            }

            var workItems = new List<WorkItem>(_batchSize);
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = _receiveSql;
                cmd.AddParameter("@Worker", worker);
                cmd.AddParameter("@Take", _batchSize);
                cmd.AddParameter("@InFlight", ((int)StatusFlags.InFlight));
                cmd.AddParameter("@VisibleOn", receiveOn.AddSeconds(Math.Max(_visibilityTimeout, 30)));
                cmd.AddParameter("@ReceiveOn", receiveOn);
                cmd.AddParameter("@MaxAttempts", _maxAttempts);
                cmd.AddParameter("@DeleteOn", deleteOn);

                // dynamic parameters
                for (var i = 0; i < _queues.Length; i++)
                {
                    cmd.Parameters.AddWithValue($"@Queue{i}", _queues[i]);
                }

                using (var rdr = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await rdr.ReadAsync(cancellationToken))
                    {
                        var workItem = new WorkItem
                        {
                            Attempts = rdr.Map<int>(nameof(WorkItem.Attempts).ToLowerInvariant()),
                            CorrelationId = rdr.Map<Guid>(nameof(WorkItem.CorrelationId).ToLowerInvariant()),
                            ExpireOn = rdr.Map<DateTime>(nameof(WorkItem.ExpireOn).ToLowerInvariant()),
                            Id = rdr.Map<Guid>(nameof(WorkItem.Id).ToLowerInvariant()),
                            Name = rdr.Map<string>(nameof(WorkItem.Name).ToLowerInvariant()),
                            Payload = rdr.Map<byte[]>(nameof(WorkItem.Payload).ToLowerInvariant()),
                            Queue = rdr.Map<string>(nameof(WorkItem.Queue).ToLowerInvariant()),
                            ScheduleId = rdr.Map<Guid?>(nameof(WorkItem.ScheduleId).ToLowerInvariant())
                        };
                        workItem.Token = workItem.Id.ToString();
                        workItems.Add(workItem);
                    }
                }
            }, cancellationToken);
            return workItems;
        }

        public async Task<DateTime> RenewAsync(WorkItem item, CancellationToken cancellationToken)
        {
            var lockExpireOn = (item.VisibleOn ?? DateTime.UtcNow).AddSeconds(Math.Max(_visibilityTimeout, 30));
            await ExecuteAsync(async cmd =>
            {
                await cmd.PrepareForRenewAsync(item, lockExpireOn, cancellationToken);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
            return lockExpireOn;
        }

        public async Task CompleteAsync(WorkItem item, CancellationToken cancellationToken)
        {
            await ExecuteAsync(async cmd =>
            {
                await cmd.PrepareForCompleteAsync(item, cancellationToken);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
        }

        public async Task FaultAsync(WorkItem item, CancellationToken cancellationToken)
        {
            await ExecuteAsync(async cmd =>
            {
                await cmd.PrepareForFaultAsync(item, _visibilityTimeout, cancellationToken);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
        }

        public void Dispose()
        {
        }

        private Task ExecuteAsync(Func<NpgsqlCommand, Task> func, CancellationToken cancellationToken)
        {
            return PostgresRunner.ExecuteAsync(func, _connectionString, true, cancellationToken);
        }
    }
}
