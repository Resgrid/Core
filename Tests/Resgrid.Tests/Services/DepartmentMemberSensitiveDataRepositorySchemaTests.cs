using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentMemberSensitiveDataRepositorySchemaTests
	{
		[Test]
		[TestCase(DatabaseTypes.SqlServer, true)]
		[TestCase(DatabaseTypes.SqlServer, false)]
		[TestCase(DatabaseTypes.Postgres, true)]
		[TestCase(DatabaseTypes.Postgres, false)]
		public async Task Outstanding_legacy_profile_query_matches_the_deployed_schema(
			DatabaseTypes databaseType, bool legacyIdentificationNumberExists)
		{
			var originalDatabaseType = DataConfig.DatabaseType;
			DataConfig.DatabaseType = databaseType;

			try
			{
				var executedSql = new List<string>();
				var connection = new CapturingConnection(executedSql,
					scalarResult: legacyIdentificationNumberExists ? 1L : 0L);
				var unitOfWork = new Mock<IUnitOfWork>();
				unitOfWork.Setup(x => x.Connection).Returns(connection);
				unitOfWork.Setup(x => x.CreateOrGetConnection()).Returns(connection);

				SqlConfiguration configuration = databaseType == DatabaseTypes.Postgres
					? new PostgreSqlConfiguration()
					: new SqlServerConfiguration();

				var repository = new DepartmentMemberSensitiveDataRepository(
					Mock.Of<IConnectionProvider>(), configuration, unitOfWork.Object, Mock.Of<IQueryFactory>());

				await repository.GetDepartmentIdsWithOutstandingLegacyProfileDataAsync();

				executedSql.Should().HaveCount(2);
				executedSql[0].Should().ContainEquivalentOf("information_schema.columns");
				executedSql[1].Should().ContainEquivalentOf("homeaddressid");
				executedSql[1].Should().ContainEquivalentOf("mailingaddressid");

				var identificationNumberReference = databaseType == DatabaseTypes.Postgres
					? "up.identificationnumber"
					: "up.[IdentificationNumber]";

				if (legacyIdentificationNumberExists)
					executedSql[1].Should().ContainEquivalentOf(identificationNumberReference);
				else
					executedSql[1].Should().NotContainEquivalentOf(identificationNumberReference);
			}
			finally
			{
				DataConfig.DatabaseType = originalDatabaseType;
			}
		}
	}
}
