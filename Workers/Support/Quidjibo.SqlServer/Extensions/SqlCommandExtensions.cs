// Copyright (c) smiggleworth. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Quidjibo.Models;
using Quidjibo.SqlServer.Providers;
using Quidjibo.SqlServer.Utils;

namespace Quidjibo.SqlServer.Extensions
{
    public static class SqlCommandExtensions
    {
        public static async Task PrepareForSendAsync(this SqlCommand cmd, WorkItem item, int delay, CancellationToken cancellationToken)
        {
            var createdOn = DateTime.UtcNow;
            var visibleOn = createdOn.AddSeconds(delay);
            var expireOn = item.ExpireOn == default(DateTime) ? visibleOn.AddDays(SqlWorkProvider.DEFAULT_EXPIRE_DAYS) : item.ExpireOn;

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = await SqlLoader.GetScript("Work.Send");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            cmd.AddParameter("@Id", item.Id);
            cmd.AddParameter("@ScheduleId", item.ScheduleId);
            cmd.AddParameter("@CorrelationId", item.CorrelationId);
            cmd.AddParameter("@Name", item.Name);
            cmd.AddParameter("@Worker", item.Worker);
            cmd.AddParameter("@Queue", item.Queue);
            cmd.AddParameter("@Attempts", item.Attempts);
            cmd.AddParameter("@CreatedOn", createdOn);
            cmd.AddParameter("@ExpireOn", expireOn);
            cmd.AddParameter("@VisibleOn", visibleOn);
            cmd.AddParameter("@Status", SqlWorkProvider.StatusFlags.New);
            cmd.AddParameter("@Payload", item.Payload);
        }

        public static async Task PrepareForRenewAsync(this SqlCommand cmd, WorkItem item, DateTime lockExpireOn, CancellationToken cancellationToken)
        {
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                cmd.CommandText = await SqlLoader.GetScript("Work.Renew");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
                cmd.AddParameter("@Id", item.Id);
                cmd.AddParameter("@VisibleOn", lockExpireOn);
        }

        public static async Task PrepareForCompleteAsync(this SqlCommand cmd, WorkItem item, CancellationToken cancellationToken)
        {
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = await SqlLoader.GetScript("Work.Complete");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            cmd.AddParameter("@Id", item.Id);
            cmd.AddParameter("@Complete", SqlWorkProvider.StatusFlags.Complete);
        }

        public static async Task PrepareForFaultAsync(this SqlCommand cmd, WorkItem item, int visibilityTimeout, CancellationToken cancellationToken)
        {
            var faultedOn = DateTime.UtcNow;

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            cmd.CommandText = await SqlLoader.GetScript("Work.Fault");
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            cmd.AddParameter("@Id", item.Id);
            cmd.AddParameter("@VisibleOn", faultedOn.AddSeconds(Math.Max(visibilityTimeout, 30)));
            cmd.AddParameter("@Faulted", SqlWorkProvider.StatusFlags.Faulted);
        }

    }

}