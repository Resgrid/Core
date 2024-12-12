// Copyright (c) smiggleworth. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Quidjibo.Models;
using Quidjibo.Postgres.Providers;
using Quidjibo.Postgres.Utils;

namespace Quidjibo.Postgres.Extensions
{
    public static class NpgsqlCommandExtensions
    {
        public static async Task PrepareForSendAsync(this NpgsqlCommand cmd, WorkItem item, int delay, CancellationToken cancellationToken)
        {
            var createdOn = DateTime.UtcNow;
            var visibleOn = createdOn.AddSeconds(delay);
            var expireOn = item.ExpireOn == default(DateTime) ? visibleOn.AddDays(PostgresWorkProvider.DEFAULT_EXPIRE_DAYS) : item.ExpireOn;

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = await SqlLoader.GetScript("Work.Send");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
			cmd.Parameters.AddWithValue("@Id", item.Id);
            cmd.Parameters.AddWithValue("@ScheduleId", item.ScheduleId);
            cmd.Parameters.AddWithValue("@CorrelationId", item.CorrelationId);
            cmd.Parameters.AddWithValue("@Name", item.Name);

			if (item.Worker == null)
				cmd.Parameters.AddWithValue("@Worker", NpgsqlDbType.Varchar, DBNull.Value);
			else
				cmd.Parameters.AddWithValue("@Worker", NpgsqlDbType.Varchar, item.Worker);

			cmd.Parameters.AddWithValue("@Queue", item.Queue);
            cmd.Parameters.AddWithValue("@Attempts", item.Attempts);
            cmd.Parameters.AddWithValue("@CreatedOn", createdOn);
            cmd.Parameters.AddWithValue("@ExpireOn", expireOn);
            cmd.Parameters.AddWithValue("@VisibleOn", visibleOn);
            cmd.Parameters.AddWithValue("@Status", ((int)PostgresWorkProvider.StatusFlags.New));
            cmd.Parameters.AddWithValue("@Payload", item.Payload);

		}

        public static async Task PrepareForRenewAsync(this NpgsqlCommand cmd, WorkItem item, DateTime lockExpireOn, CancellationToken cancellationToken)
        {
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                cmd.CommandText = await SqlLoader.GetScript("Work.Renew");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
                cmd.Parameters.AddWithValue("@Id", item.Id);
                cmd.Parameters.AddWithValue("@VisibleOn", lockExpireOn);
        }

        public static async Task PrepareForCompleteAsync(this NpgsqlCommand cmd, WorkItem item, CancellationToken cancellationToken)
        {
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = await SqlLoader.GetScript("Work.Complete");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            cmd.Parameters.AddWithValue("@Id", item.Id);
            cmd.Parameters.AddWithValue("@Complete", ((int)PostgresWorkProvider.StatusFlags.Complete));
        }

        public static async Task PrepareForFaultAsync(this NpgsqlCommand cmd, WorkItem item, int visibilityTimeout, CancellationToken cancellationToken)
        {
            var faultedOn = DateTime.UtcNow;

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = await SqlLoader.GetScript("Work.Fault");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            cmd.Parameters.AddWithValue("@Id", item.Id);
            cmd.Parameters.AddWithValue("@VisibleOn", faultedOn.AddSeconds(Math.Max(visibilityTimeout, 30)));
            cmd.Parameters.AddWithValue("@Faulted", ((int)PostgresWorkProvider.StatusFlags.Faulted));
        }

    }

}
