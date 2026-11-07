using FluentAssertions;
using NUnit.Framework;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calls;
using Resgrid.Repositories.DataRepository.Queries.WeatherAlerts;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// The badge counts must count exactly the rows the lists show: the calls badge the v4 GetActiveCalls rows (active,
	/// not deleted), the weather badge the active Extreme/Severe alerts that have not expired. Pin both dialects.
	/// </summary>
	[TestFixture]
	public class BadgeCountQueryTests
	{
		[Test]
		public void SqlServer_active_calls_count_matches_the_open_calls_list()
		{
			var configuration = new SqlServerConfiguration();
			var count = new SelectActiveCallsCountByDidQuery(configuration).GetQuery();

			count.Trim().Should().StartWith("SELECT COUNT(*) FROM");
			count.Should().Contain("[DepartmentId] = @DepartmentId AND [IsDeleted] = 0 AND [State] = 0");
			configuration.SelectAllOpenCallsByDidDateQuery.Should().EndWith("WHERE [DepartmentId] = %DID% AND [IsDeleted] = 0 AND [State] = 0");
		}

		[Test]
		public void Postgres_active_calls_count_matches_the_open_calls_list()
		{
			var configuration = new PostgreSqlConfiguration();
			var count = new SelectActiveCallsCountByDidQuery(configuration).GetQuery();

			count.Trim().Should().StartWith("SELECT COUNT(*) FROM");
			count.Should().Contain("DepartmentId = @DepartmentId AND IsDeleted = false AND State = 0");
			configuration.SelectAllOpenCallsByDidDateQuery.Should().EndWith("WHERE DepartmentId = %DID% AND IsDeleted = false AND State = 0");
		}

		/// <summary>"Calls I'm on" is the apps' isCurrentUserOnCall rule: the member, their group, a role they hold, a unit seat.</summary>
		[Test]
		public void Calls_dispatched_to_a_member_cover_every_dispatch_kind_in_both_dialects()
		{
			foreach (SqlConfiguration configuration in new SqlConfiguration[] { new SqlServerConfiguration(), new PostgreSqlConfiguration() })
			{
				var query = new SelectActiveCallsCountDispatchedToUserQuery(configuration).GetQuery().Replace("[", string.Empty).Replace("]", string.Empty).ToLowerInvariant();

				query.Should().Contain("select count(*) from");
				query.Should().Contain("c.state = 0");
				query.Should().Contain("calldispatches d where d.callid = c.callid and d.userid = @userid");
				query.Should().Contain("calldispatchgroups g inner join");
				query.Should().Contain("departmentgroupmembers m on m.departmentgroupid = g.departmentgroupid");
				query.Should().Contain("personnelroleusers u on u.personnelroleid = r.roleid");
				query.Should().Contain("unitactiveroles ar on ar.unitid = du.unitid");
				query.Should().NotContain("%");
			}
		}

		[Test]
		public void Weather_counts_only_active_unexpired_alerts_per_severity_in_both_dialects()
		{
			foreach (SqlConfiguration configuration in new SqlConfiguration[] { new SqlServerConfiguration(), new PostgreSqlConfiguration() })
			{
				var query = new SelectActiveWeatherAlertSeverityCountsByDepartmentIdQuery(configuration).GetQuery().Replace("[", string.Empty).Replace("]", string.Empty);

				query.Should().Contain("COUNT(*) AS AlertCount");
				query.Should().Contain("DepartmentId = @DepartmentId AND Status = 0 AND Severity <= @MaxSeverity");
				query.Should().Contain("(ExpiresUtc IS NULL OR ExpiresUtc > @Now)");
				query.Should().EndWith("GROUP BY Severity");
			}
		}
	}
}
