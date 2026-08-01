using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Repositories.NoSqlRepository;

namespace Resgrid.Tests.Repositories
{
	[TestFixture]
	public class MongoRepositoryConfigurationTests
	{
		private string _originalConnectionString;
		private string _originalApplicationName;
		private int _originalServerSelectionTimeoutSeconds;
		private int _originalConnectTimeoutSeconds;
		private int _originalSocketTimeoutSeconds;

		[SetUp]
		public void SetUp()
		{
			_originalConnectionString = DataConfig.NoSqlConnectionString;
			_originalApplicationName = DataConfig.NoSqlApplicationName;
			_originalServerSelectionTimeoutSeconds = DataConfig.NoSqlServerSelectionTimeoutSeconds;
			_originalConnectTimeoutSeconds = DataConfig.NoSqlConnectTimeoutSeconds;
			_originalSocketTimeoutSeconds = DataConfig.NoSqlSocketTimeoutSeconds;
		}

		[TearDown]
		public void TearDown()
		{
			DataConfig.NoSqlConnectionString = _originalConnectionString;
			DataConfig.NoSqlApplicationName = _originalApplicationName;
			DataConfig.NoSqlServerSelectionTimeoutSeconds = _originalServerSelectionTimeoutSeconds;
			DataConfig.NoSqlConnectTimeoutSeconds = _originalConnectTimeoutSeconds;
			DataConfig.NoSqlSocketTimeoutSeconds = _originalSocketTimeoutSeconds;
		}

		[Test]
		public void Constructor_UsesConfiguredMongoTimeouts()
		{
			// Arrange
			DataConfig.NoSqlConnectionString = "mongodb://localhost:27017";
			DataConfig.NoSqlApplicationName = "Resgrid.Tests";
			DataConfig.NoSqlServerSelectionTimeoutSeconds = 2;
			DataConfig.NoSqlConnectTimeoutSeconds = 3;
			DataConfig.NoSqlSocketTimeoutSeconds = 4;

			// Act
			var repository = new MongoRepository<UnitsLocation>();
			var settings = repository.GetCollection().Database.Client.Settings;

			// Assert
			settings.ApplicationName.Should().Be("Resgrid.Tests");
			settings.ServerSelectionTimeout.Should().Be(TimeSpan.FromSeconds(2));
			settings.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(3));
			settings.SocketTimeout.Should().Be(TimeSpan.FromSeconds(4));
		}
	}
}
