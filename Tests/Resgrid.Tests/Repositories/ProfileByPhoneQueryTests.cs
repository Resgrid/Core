using FluentAssertions;
using NUnit.Framework;
using Resgrid.Repositories.DataRepository.Queries.UserProfiles;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Profiles are saved in E.164 (+12015550123) while older rows hold the bare digits, so the lookup
	/// has to match the stored value with and without the leading "+". Matching the column directly
	/// keeps the predicate sargable - wrapping MobileNumber/HomeNumber in REPLACE() to normalize it
	/// would push this hot inbound-webhook lookup toward a scan of UserProfiles.
	///
	/// Only the "+" belongs here. The country-code variant is a separate candidate that
	/// UserProfileService tries as an ordered second pass, because 2015550123 and 12015550123 can be
	/// two different profiles and UserProfilesRepository takes FirstOrDefault() with no ORDER BY.
	/// </summary>
	[TestFixture]
	public class ProfileByPhoneQueryTests
	{
		[Test]
		public void SqlServer_profile_by_mobile_matches_the_stored_number_with_and_without_the_plus()
		{
			var query = new SelectProfileByMobileQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("[MobileNumber] IN (@MobileNumber, '+' + @MobileNumber)");
			query.Should().NotContain("REPLACE(");
			query.Should().NotContain("'1' + @MobileNumber");
		}

		[Test]
		public void SqlServer_profile_by_home_matches_the_stored_number_with_and_without_the_plus()
		{
			var query = new SelectProfileByHomeQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("[HomeNumber] IN (@HomeNumber, '+' + @HomeNumber)");
			query.Should().NotContain("REPLACE(");
			query.Should().NotContain("'1' + @HomeNumber");
		}

		[Test]
		public void SqlServer_profile_by_mobile_ranks_verified_profiles_first()
		{
			var query = new SelectProfileByMobileQuery(new SqlServerConfiguration()).GetQuery();

			// The same number can sit on a stale or mistyped profile as well as the real owner's, and
			// the repository takes FirstOrDefault() - without this ORDER BY which one comes back is up
			// to the plan. Verified first, then grandfathered (NULL), then never-verified.
			query.Should().Contain("ORDER BY");
			query.Should().Contain("WHEN [dbo].UserProfiles.[MobileNumberVerified] = 1 THEN 0");
			query.Should().Contain("WHEN [dbo].UserProfiles.[MobileNumberVerified] IS NULL THEN 1");
			query.Should().Contain("ELSE 2");

			// Ties still have to resolve to the same row every time.
			query.Should().Contain("[dbo].UserProfiles.[UserProfileId] DESC");
		}

		[Test]
		public void SqlServer_profile_by_home_ranks_verified_profiles_first()
		{
			var query = new SelectProfileByHomeQuery(new SqlServerConfiguration()).GetQuery();

			query.Should().Contain("WHEN [dbo].UserProfiles.[HomeNumberVerified] = 1 THEN 0");
			query.Should().Contain("WHEN [dbo].UserProfiles.[HomeNumberVerified] IS NULL THEN 1");
			query.Should().Contain("[dbo].UserProfiles.[UserProfileId] DESC");
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
