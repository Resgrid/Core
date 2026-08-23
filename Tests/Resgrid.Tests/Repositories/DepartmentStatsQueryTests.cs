using FluentAssertions;
using NUnit.Framework;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Departments;
using Resgrid.Repositories.DataRepository.Queries.Messages;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// The top nav unread badge is fed by the DepartmentStats query, not by the dedicated unread
	/// message count query. When the two drift apart the badge counts read (and expired) messages
	/// and never goes back down, so pin the unread predicates here.
	/// </summary>
	[TestFixture]
	public class DepartmentStatsQueryTests
	{
		[Test]
		public void SqlServer_department_stats_unread_count_only_counts_unread_unexpired_messages()
		{
			var query = new SelectDepartmentStatsByUserDidQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("mr.[ReadOn] IS NULL");
			query.Should().Contain("m.[ExpireOn] IS NULL OR m.[ExpireOn] > @CurrentDate");
		}

		[Test]
		public void Postgres_department_stats_unread_count_only_counts_unread_unexpired_messages()
		{
			var query = new SelectDepartmentStatsByUserDidQuery(new PostgreSqlConfiguration()).GetQuery();

			query.Should().Contain("mr.ReadOn IS NULL");
			query.Should().Contain("m.ExpireOn IS NULL OR m.ExpireOn > @CurrentDate");
		}

		[Test]
		public void Department_stats_unread_predicates_match_the_dedicated_unread_count_query()
		{
			var configurations = new SqlConfiguration[] { new SqlServerConfiguration(), new PostgreSqlConfiguration() };

			foreach (var configuration in configurations)
			{
				var statsQuery = new SelectDepartmentStatsByUserDidQuery(configuration).GetQuery();
				var unreadQuery = new SelectUnreadMessageCountQuery(configuration).GetQuery();

				var readOnPredicate = configuration is SqlServerConfiguration ? "mr.[ReadOn] IS NULL" : "mr.ReadOn IS NULL";

				unreadQuery.Should().Contain(readOnPredicate);
				statsQuery.Should().Contain(readOnPredicate);
			}
		}
	}
}
