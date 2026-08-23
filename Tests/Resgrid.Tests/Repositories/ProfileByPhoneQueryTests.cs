using FluentAssertions;
using NUnit.Framework;
using Resgrid.Repositories.DataRepository.Queries.UserProfiles;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// The stored MobileNumber/HomeNumber columns hold E.164 values (+12248304555) while callers hand
	/// in bare digits, so the compare has to normalize the column, not just the parameter. An exact
	/// string compare here silently breaks inbound SMS and voice identification.
	/// </summary>
	[TestFixture]
	public class ProfileByPhoneQueryTests
	{
		[Test]
		public void SqlServer_profile_by_mobile_normalizes_the_stored_column()
		{
			var query = new SelectProfileByMobileQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().NotContain("WHERE [MobileNumber] = @MobileNumber");
			query.Should().Contain("REPLACE([MobileNumber], '+', '')");
			query.Should().Contain("IN (@MobileNumber, '1' + @MobileNumber)");
		}

		[Test]
		public void SqlServer_profile_by_home_normalizes_the_stored_column()
		{
			var query = new SelectProfileByHomeQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().NotContain("WHERE [HomeNumber] = @HomeNumber");
			query.Should().Contain("REPLACE([HomeNumber], '+', '')");
			query.Should().Contain("IN (@HomeNumber, '1' + @HomeNumber)");
		}

		[Test]
		public void SqlServer_profile_by_phone_queries_ignore_blank_stored_numbers()
		{
			new SelectProfileByMobileQuery(new SqlServerConfiguration()).GetQuery()
				.Should().Contain("[MobileNumber] IS NOT NULL AND [MobileNumber] <> ''");

			new SelectProfileByHomeQuery(new SqlServerConfiguration()).GetQuery()
				.Should().Contain("[HomeNumber] IS NOT NULL AND [HomeNumber] <> ''");
		}
	}
}
