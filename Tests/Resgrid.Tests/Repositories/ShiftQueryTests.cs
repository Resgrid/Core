using FluentAssertions;
using NUnit.Framework;
using Resgrid.Repositories.DataRepository.Queries.Shifts;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	[TestFixture]
	public class ShiftQueryTests
	{
		[Test]
		public void Open_trade_requests_match_on_the_invited_user()
		{
			var query = new SelectOpenShiftSignupTradesByUserIdQuery(new SqlServerConfiguration()).GetQuery();

			// This used to read "sst.UserId = @UserId AND sst.UserId != @UserId", which can never be true, so nobody ever
			// saw a trade they had been asked to take.
			query.Should().Contain("sstu.[UserId] = @UserId");
			query.Should().NotContain("!=");
			query.Should().Contain("[dbo].ShiftSignupTradeUsers sstu");
			query.Should().NotContain("%");
		}

		[Test]
		public void Department_trades_join_both_signups_for_the_roster()
		{
			var query = new SelectShiftSignupTradesByDepartmentIdQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("SELECT sst.*, ss.*, ts.*");
			query.Should().Contain("LEFT JOIN [dbo].ShiftSignups ts ON ts.[ShiftSignupId] = sst.[TargetShiftSignupId]");
			query.Should().Contain("s.[DepartmentId] = @DepartmentId");
			query.Should().Contain("@StartDate");
			query.Should().NotContain("%");
		}

		[Test]
		public void Department_signups_are_bounded_by_the_date_range()
		{
			var query = new SelectShiftSignupsByDepartmentIdAndDateRangeQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("ss.[ShiftDay] >= @StartDate AND ss.[ShiftDay] < @EndDate");
			query.Should().Contain("s.[DepartmentId] = @DepartmentId");
			query.Should().NotContain("%");
		}

		[Test]
		public void Offered_trade_days_are_found_by_signup()
		{
			var query = new SelectShiftSignupTradeUserShiftsBySignupIdQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("ShiftSignupTradeUserShifts WHERE [ShiftSignupId] = @ShiftSignupId");
		}

		[Test]
		public void Postgres_department_shift_json_carries_signups_and_standing_roster_groups()
		{
			var config = new PostgreSqlConfiguration();

			// Without the alias the signups column came back as "jsonb_agg" and never reached Shift.Signups, and without
			// GroupId every standing-roster person on Postgres lost their team.
			foreach (var query in new[] { config.SelectShiftsByDidJSONQuery, config.SelectShiftByShiftIdJSONQuery, config.SelectShiftAndDaysJSONQuery, config.SelectUpcomingShiftAndDaysJSONQuery })
			{
				query.Should().Contain(") signups");
				query.Should().Contain("'GroupId', sp.groupid");
				query.Should().Contain("'ApprovalPending', ss.approvalpending");
			}
		}
	}
}
