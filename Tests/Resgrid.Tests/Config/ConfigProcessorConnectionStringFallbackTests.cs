using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;

namespace Resgrid.Tests.Config
{
	/// <summary>
	/// GitHub #536: a SQL Server install that only sets DataConfig.ConnectionString left CoreConnectionString empty,
	/// and FluentMigrator ran the upgrade on its connectionless preview processor (no DDL, then NotImplementedException).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ConfigProcessorConnectionStringFallbackTests
	{
		private DatabaseTypes _databaseType;
		private string _connectionString;
		private string _coreConnectionString;

		[SetUp]
		public void SetUp()
		{
			_databaseType = DataConfig.DatabaseType;
			_connectionString = DataConfig.ConnectionString;
			_coreConnectionString = DataConfig.CoreConnectionString;

			DataConfig.DatabaseType = DatabaseTypes.SqlServer;
			DataConfig.ConnectionString = "Server=default;";
			DataConfig.CoreConnectionString = "";
			ResetDerivedValue();
		}

		[TearDown]
		public void TearDown()
		{
			DataConfig.DatabaseType = _databaseType;
			DataConfig.ConnectionString = _connectionString;
			DataConfig.CoreConnectionString = _coreConnectionString;
			ResetDerivedValue();
		}

		[Test]
		public void sql_server_core_connection_string_follows_connection_string_when_unset()
		{
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:ConnectionString", "Server=db;Database=Resgrid;")));

			DataConfig.CoreConnectionString.Should().Be("Server=db;Database=Resgrid;");
		}

		[Test]
		public void explicit_core_connection_string_is_kept()
		{
			ConfigProcessor.LoadAndProcessEnvVariables(Env(
				("RESGRID:DataConfig:ConnectionString", "Server=db;Database=Resgrid;"),
				("RESGRID:DataConfig:CoreConnectionString", "Server=core;Database=Resgrid;")));

			DataConfig.CoreConnectionString.Should().Be("Server=core;Database=Resgrid;");
		}

		[Test]
		public void a_later_connection_string_override_replaces_the_derived_value()
		{
			// Config file pass, then the environment variable pass overriding ConnectionString.
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:ConnectionString", "Server=file;")));
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:ConnectionString", "Server=env;")));

			DataConfig.CoreConnectionString.Should().Be("Server=env;");
		}

		[Test]
		public void a_later_explicit_core_connection_string_wins_over_the_derived_value()
		{
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:ConnectionString", "Server=file;")));
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:CoreConnectionString", "Server=core;")));
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:ConnectionString", "Server=other;")));

			DataConfig.CoreConnectionString.Should().Be("Server=core;");
		}

		[Test]
		public void postgres_is_never_given_the_sql_server_connection_string()
		{
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:ConnectionString", "Server=db;")));
			ConfigProcessor.LoadAndProcessEnvVariables(Env(("RESGRID:DataConfig:DatabaseType", "1")));

			DataConfig.DatabaseType.Should().Be(DatabaseTypes.Postgres);
			DataConfig.CoreConnectionString.Should().BeEmpty();
		}

		private static IEnumerable<KeyValuePair<string, string>> Env(params (string Key, string Value)[] values)
		{
			foreach (var (key, value) in values)
				yield return new KeyValuePair<string, string>(key, value);
		}

		private static void ResetDerivedValue()
		{
			typeof(ConfigProcessor)
				.GetField("_derivedCoreConnectionString", BindingFlags.NonPublic | BindingFlags.Static)!
				.SetValue(null, null);
		}
	}
}
