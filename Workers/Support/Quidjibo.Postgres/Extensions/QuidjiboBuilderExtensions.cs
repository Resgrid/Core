using System;
using Quidjibo.Constants;
using Quidjibo.Postgres.Configurations;
using Quidjibo.Postgres.Factories;

namespace Quidjibo.Postgres.Extensions
{
    public static class QuidjiboBuilderExtensions
    {
        /// <summary>
        ///     Use Sql Server for Work, Progress, an Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="sqlServerQuidjiboConfiguration"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UsePostgres(this QuidjiboBuilder builder, Action<PostgresQuidjiboConfiguration> sqlServerQuidjiboConfiguration)
        {
            var config = new PostgresQuidjiboConfiguration();
            sqlServerQuidjiboConfiguration(config);
            return builder.UsePostgres(config);
        }

        /// <summary>
        ///     Use Sql Server for Work, Progress, and Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="sqlServerQuidjiboConfiguration"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UsePostgres(this QuidjiboBuilder builder, PostgresQuidjiboConfiguration sqlServerQuidjiboConfiguration)
        {
            return builder.Configure(sqlServerQuidjiboConfiguration)
                          .ConfigureWorkProviderFactory(new PostgresWorkProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration))
                          .ConfigureProgressProviderFactory(new PostgresProgressProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration.ConnectionString))
                          .ConfigureScheduleProviderFactory(new PostgresScheduleProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration.ConnectionString));
        }

        /// <summary>
        ///     Use Sql Server for Work, Progress, and Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="connectionString"></param>
        /// <param name="queues"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UsePostgres(this QuidjiboBuilder builder, string connectionString, params string[] queues)
        {
            if (queues == null || queues.Length == 0)
            {
                queues = Default.Queues;
            }

            var config = new PostgresQuidjiboConfiguration
            {
                ConnectionString = connectionString,
                Queues = queues
            };
            return builder.Configure(config)
                          .ConfigureWorkProviderFactory(new PostgresWorkProviderFactory(builder.LoggerFactory, config))
                          .ConfigureProgressProviderFactory(new PostgresProgressProviderFactory(builder.LoggerFactory, connectionString))
                          .ConfigureScheduleProviderFactory(new PostgresScheduleProviderFactory(builder.LoggerFactory, connectionString));
        }

        /// <summary>
        ///     Use Sql Server For Work
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="sqlServerQuidjiboConfiguration"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UsePostgresForWork(this QuidjiboBuilder builder, PostgresQuidjiboConfiguration sqlServerQuidjiboConfiguration)
        {
            return builder.ConfigureWorkProviderFactory(new PostgresWorkProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration));
        }

        /// <summary>
        ///     Use Sql Server For Progress
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UsePostgresForProgress(this QuidjiboBuilder builder, string connectionString)
        {
            return builder.ConfigureProgressProviderFactory(new PostgresProgressProviderFactory(builder.LoggerFactory, connectionString));
        }

        /// <summary>
        ///     Use Sql Server For Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UsePostgresForSchedule(this QuidjiboBuilder builder, string connectionString)
        {
            return builder.ConfigureScheduleProviderFactory(new PostgresScheduleProviderFactory(builder.LoggerFactory, connectionString));
        }
    }
}
