using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quidjibo.Models;
using Quidjibo.Providers;
using Quidjibo.SqlServer.Extensions;
using Quidjibo.SqlServer.Utils;

namespace Quidjibo.SqlServer.Providers
{
    public class SqlProgressProvider : IProgressProvider
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;

        public SqlProgressProvider(
            ILogger logger,
            string connectionString)
        {
            _logger = logger;
            _connectionString = connectionString;
        }

        public async Task ReportAsync(ProgressItem item, CancellationToken cancellationToken)
        {
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Progress.Create");
                cmd.AddParameter("@Id", item.Id);
                cmd.AddParameter("@WorkId", item.WorkId);
                cmd.AddParameter("@CorrelationId", item.CorrelationId);
                cmd.AddParameter("@Name", item.Name);
                cmd.AddParameter("@Queue", item.Queue);
                cmd.AddParameter("@RecordedOn", item.RecordedOn);
                cmd.AddParameter("@Value", item.Value);
                cmd.AddParameter("@Note", item.Note);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);
            await Task.CompletedTask;
        }

        public async Task<List<ProgressItem>> LoadByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken)
        {
            var items = new List<ProgressItem>();
            await ExecuteAsync(async cmd =>
            {
                cmd.CommandText = await SqlLoader.GetScript("Progress.LoadByCorrelationId");
                cmd.AddParameter("@CorrelationId", correlationId);
                using (var rdr = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await rdr.ReadAsync(cancellationToken))
                    {
                        var workItem = new ProgressItem
                        {
                            CorrelationId = rdr.Map<Guid>(nameof(ProgressItem.CorrelationId)),
                            Id = rdr.Map<Guid>(nameof(ProgressItem.Id)),
                            Name = rdr.Map<string>(nameof(ProgressItem.Name)),
                            Note = rdr.Map<string>(nameof(ProgressItem.Note)),
                            Queue = rdr.Map<string>(nameof(ProgressItem.Queue)),
                            RecordedOn = rdr.Map<DateTime>(nameof(ProgressItem.RecordedOn)),
                            Value = rdr.Map<int>(nameof(ProgressItem.Value)),
                            WorkId = rdr.Map<Guid>(nameof(ProgressItem.WorkId))
                        };
                        items.Add(workItem);
                    }
                }
            }, cancellationToken);
            return items;
        }

        private Task ExecuteAsync(Func<SqlCommand, Task> func, CancellationToken cancellationToken)
        {
            return SqlRunner.ExecuteAsync(func, _connectionString, true, cancellationToken);
        }
    }
}