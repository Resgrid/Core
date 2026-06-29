using System;
using Quidjibo.Constants;
using Quidjibo.SqlServer.Configurations;
using Quidjibo.SqlServer.Factories;

namespace Quidjibo.SqlServer.Extensions
{
    public static class QuidjiboBuilderExtensions
    {
        /// <summary>
        ///     Use Sql Server for Work, Progress, an Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="sqlServerQuidjiboConfiguration"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UseSqlServer(this QuidjiboBuilder builder, Action<SqlServerQuidjiboConfiguration> sqlServerQuidjiboConfiguration)
        {
            var config = new SqlServerQuidjiboConfiguration();
            sqlServerQuidjiboConfiguration(config);
            return builder.UseSqlServer(config);
        }

        /// <summary>
        ///     Use Sql Server for Work, Progress, and Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="sqlServerQuidjiboConfiguration"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UseSqlServer(this QuidjiboBuilder builder, SqlServerQuidjiboConfiguration sqlServerQuidjiboConfiguration)
        {
            return builder.Configure(sqlServerQuidjiboConfiguration)
                          .ConfigureWorkProviderFactory(new SqlWorkProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration))
                          .ConfigureProgressProviderFactory(new SqlProgressProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration.ConnectionString))
                          .ConfigureScheduleProviderFactory(new SqlScheduleProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration.ConnectionString));
        }

        /// <summary>
        ///     Use Sql Server for Work, Progress, and Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="connectionString"></param>
        /// <param name="queues"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UseSqlServer(this QuidjiboBuilder builder, string connectionString, params string[] queues)
        {
            if (queues == null || queues.Length == 0)
            {
                queues = Default.Queues;
            }

            var config = new SqlServerQuidjiboConfiguration
            {
                ConnectionString = connectionString,
                Queues = queues
            };
            return builder.Configure(config)
                          .ConfigureWorkProviderFactory(new SqlWorkProviderFactory(builder.LoggerFactory, config))
                          .ConfigureProgressProviderFactory(new SqlProgressProviderFactory(builder.LoggerFactory, connectionString))
                          .ConfigureScheduleProviderFactory(new SqlScheduleProviderFactory(builder.LoggerFactory, connectionString));
        }

        /// <summary>
        ///     Use Sql Server For Work
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="sqlServerQuidjiboConfiguration"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UseSqlServerForWork(this QuidjiboBuilder builder, SqlServerQuidjiboConfiguration sqlServerQuidjiboConfiguration)
        {
            return builder.ConfigureWorkProviderFactory(new SqlWorkProviderFactory(builder.LoggerFactory, sqlServerQuidjiboConfiguration));
        }

        /// <summary>
        ///     Use Sql Server For Progress
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UseSqlServerForProgress(this QuidjiboBuilder builder, string connectionString)
        {
            return builder.ConfigureProgressProviderFactory(new SqlProgressProviderFactory(builder.LoggerFactory, connectionString));
        }

        /// <summary>
        ///     Use Sql Server For Scheduled Jobs
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static QuidjiboBuilder UseSqlServerForSchedule(this QuidjiboBuilder builder, string connectionString)
        {
            return builder.ConfigureScheduleProviderFactory(new SqlScheduleProviderFactory(builder.LoggerFactory, connectionString));
        }
    }
}