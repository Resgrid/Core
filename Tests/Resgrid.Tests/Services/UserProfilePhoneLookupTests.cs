using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Profiles are persisted in E.164 (+12015550123) by the profile save flow, while inbound SMS and
	/// voice hand us the number in whatever shape the carrier used. The query matches the stored value
	/// with and without the leading "+"; the service covers the country code being present on one side
	/// but not the other, and strips the formatting off the inbound number.
	/// </summary>
	[TestFixture]
	public class UserProfilePhoneLookupTests
	{
		private Mock<IUserProfilesRepository> _repository;
		private UserProfileService _service;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IUserProfilesRepository>();
			_service = new UserProfileService(_repository.Object, new Mock<ICacheProvider>().Object,
				new Mock<IChatbotIdentityRepository>().Object, new Mock<IDepartmentMembersRepository>().Object);
		}

		[TestCase("+12015550123", "12015550123")]
		[TestCase("12015550123", "12015550123")]
		[TestCase("(201) 555-0123", "2015550123")]
		[TestCase("201.555.0123", "2015550123")]
		[TestCase(" +1 201 555 0123 ", "12015550123")]
		public async Task GetProfileByMobileNumberAsync_strips_formatting_before_hitting_the_repository(string inbound, string expected)
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync(expected)).ReturnsAsync(profile);

			var result = await _service.GetProfileByMobileNumberAsync(inbound);

			result.Should().BeSameAs(profile);
			_repository.Verify(x => x.GetProfileByMobileNumberAsync(expected), Times.Once);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_falls_back_to_the_number_without_the_country_code()
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync((UserProfile)null);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2015550123")).ReturnsAsync(profile);

			var result = await _service.GetProfileByMobileNumberAsync("+12015550123");

			result.Should().BeSameAs(profile);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_falls_back_to_the_number_with_the_country_code()
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2015550123")).ReturnsAsync((UserProfile)null);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync(profile);

			var result = await _service.GetProfileByMobileNumberAsync("(201) 555-0123");

			result.Should().BeSameAs(profile);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_prefers_the_number_exactly_as_dialled()
		{
			// 2015550123 and 12015550123 can be two different profiles, so with nothing to separate
			// them on verification the country-code variant stays a fallback rather than being matched
			// alongside the number that was actually dialled.
			var exact = new UserProfile { UserId = "exact", MobileNumberVerified = true };
			var variant = new UserProfile { UserId = "variant", MobileNumberVerified = true };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync(exact);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2015550123")).ReturnsAsync(variant);

			var result = await _service.GetProfileByMobileNumberAsync("+12015550123");

			result.Should().BeSameAs(exact);
			_repository.Verify(x => x.GetProfileByMobileNumberAsync("2015550123"), Times.Never);
		}

		// ── Verified profiles win ─────────────────────────────────────────────────────
		// The same number can sit on a stale account, a secondary account, or one where it was
		// mistyped and never verified. Only a verified profile has proven possession of the number.

		[Test]
		public async Task GetProfileByMobileNumberAsync_prefers_a_verified_profile_over_a_closer_number_match()
		{
			var mistyped = new UserProfile { UserId = "mistyped", MobileNumberVerified = false };
			var owner = new UserProfile { UserId = "owner", MobileNumberVerified = true };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync(mistyped);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2015550123")).ReturnsAsync(owner);

			var result = await _service.GetProfileByMobileNumberAsync("+12015550123");

			result.Should().BeSameAs(owner);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_prefers_a_verified_profile_over_a_grandfathered_one()
		{
			// NULL is the grandfathered pre-verification state, not a verified one.
			var grandfathered = new UserProfile { UserId = "grandfathered", MobileNumberVerified = null };
			var owner = new UserProfile { UserId = "owner", MobileNumberVerified = true };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync(grandfathered);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2015550123")).ReturnsAsync(owner);

			var result = await _service.GetProfileByMobileNumberAsync("+12015550123");

			result.Should().BeSameAs(owner);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_still_resolves_when_nothing_is_verified()
		{
			var mistyped = new UserProfile { UserId = "mistyped", MobileNumberVerified = false };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync(mistyped);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2015550123")).ReturnsAsync((UserProfile)null);

			var result = await _service.GetProfileByMobileNumberAsync("+12015550123");

			// Callers apply their own verification gate; resolving the profile is not the same as
			// trusting it, so an unverified match is still returned rather than swallowed.
			result.Should().BeSameAs(mistyped);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_stops_at_the_first_verified_profile()
		{
			var owner = new UserProfile { UserId = "owner", MobileNumberVerified = true };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12015550123")).ReturnsAsync(owner);

			await _service.GetProfileByMobileNumberAsync("+12015550123");

			_repository.Verify(x => x.GetProfileByMobileNumberAsync("2015550123"), Times.Never);
		}

		[Test]
		public async Task GetProfileByHomeNumberAsync_prefers_a_verified_profile()
		{
			var secondary = new UserProfile { UserId = "secondary", HomeNumberVerified = false };
			var owner = new UserProfile { UserId = "owner", HomeNumberVerified = true };
			_repository.Setup(x => x.GetProfileByHomeNumberAsync("12015550123")).ReturnsAsync(secondary);
			_repository.Setup(x => x.GetProfileByHomeNumberAsync("2015550123")).ReturnsAsync(owner);

			var result = await _service.GetProfileByHomeNumberAsync("+12015550123");

			result.Should().BeSameAs(owner);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		[TestCase("+-() .")]
		public async Task GetProfileByMobileNumberAsync_never_matches_on_a_blank_number(string inbound)
		{
			var result = await _service.GetProfileByMobileNumberAsync(inbound);

			result.Should().BeNull();
			_repository.Verify(x => x.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetProfileByHomeNumberAsync_queries_the_home_number_not_the_mobile_number()
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByHomeNumberAsync("12015550123")).ReturnsAsync(profile);

			var result = await _service.GetProfileByHomeNumberAsync("+1 (201) 555-0123");

			result.Should().BeSameAs(profile);
			_repository.Verify(x => x.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetProfileByHomeNumberAsync_falls_back_to_the_number_without_the_country_code()
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByHomeNumberAsync("12015550123")).ReturnsAsync((UserProfile)null);
			_repository.Setup(x => x.GetProfileByHomeNumberAsync("2015550123")).ReturnsAsync(profile);

			var result = await _service.GetProfileByHomeNumberAsync("+12015550123");

			result.Should().BeSameAs(profile);
		}

		[TestCase(null)]
		[TestCase("")]
		public async Task GetProfileByHomeNumberAsync_never_matches_on_a_blank_number(string inbound)
		{
			var result = await _service.GetProfileByHomeNumberAsync(inbound);

			result.Should().BeNull();
			_repository.Verify(x => x.GetProfileByHomeNumberAsync(It.IsAny<string>()), Times.Never);
		}
		[Test]
		public async Task SaveProfileAsync_clears_the_department_profile_list_cache_for_every_live_membership()
		{
			// A profile row is shared by every department the user belongs to; each department's cached list must go.
			var cache = new Mock<ICacheProvider>();
			var members = new Mock<IDepartmentMembersRepository>();
			members.Setup(m => m.GetAllDepartmentMemberByUserIdAsync("user-1")).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = 7, UserId = "user-1", IsActive = true },
				new DepartmentMember { DepartmentId = 8, UserId = "user-1" },
				new DepartmentMember { DepartmentId = 9, UserId = "user-1", IsDisabled = true },
				new DepartmentMember { DepartmentId = 10, UserId = "user-1", IsDeleted = true }
			});
			_repository.Setup(r => r.GetProfileByUserIdAsync("user-1")).ReturnsAsync((UserProfile)null);
			_repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UserProfile p, CancellationToken _, bool __) => p);
			var service = new UserProfileService(_repository.Object, cache.Object, new Mock<IChatbotIdentityRepository>().Object, members.Object);

			await service.SaveProfileAsync(7, new UserProfile { UserId = "user-1", FirstName = "Ada" });

			cache.Verify(c => c.Remove("UserProfile_user-1"), Times.Once);
			cache.Verify(c => c.Remove("AllDepUserProfile_7"), Times.Once);
			cache.Verify(c => c.Remove("AllDepUserProfile_8"), Times.Once);
			cache.Verify(c => c.Remove("AllDepUserProfile_9"), Times.Never, "a disabled membership serves no profile list");
			cache.Verify(c => c.Remove("AllDepUserProfile_10"), Times.Never);
		}
	}
}
