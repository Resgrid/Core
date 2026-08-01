using System;
using MongoDB.Driver;
using Resgrid.Config;

namespace Resgrid.Repositories.NoSqlRepository
{
	internal static class MongoClientFactory
	{
		public static MongoClient Create()
		{
			var settings = MongoClientSettings.FromConnectionString(DataConfig.NoSqlConnectionString);
			settings.ApplicationName = DataConfig.NoSqlApplicationName;
			settings.ServerSelectionTimeout = GetTimeout(DataConfig.NoSqlServerSelectionTimeoutSeconds);
			settings.ConnectTimeout = GetTimeout(DataConfig.NoSqlConnectTimeoutSeconds);
			settings.SocketTimeout = GetTimeout(DataConfig.NoSqlSocketTimeoutSeconds);

			return new MongoClient(settings);
		}

		private static TimeSpan GetTimeout(int seconds)
		{
			return TimeSpan.FromSeconds(Math.Max(1, seconds));
		}
	}
}
