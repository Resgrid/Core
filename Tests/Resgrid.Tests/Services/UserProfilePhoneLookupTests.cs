using System.Threading.Tasks;
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
	/// Profiles are persisted in E.164 (+12248304555) by the profile save flow, while inbound SMS and
	/// voice hand us the number in whatever shape the carrier used. Both sides have to be reduced to
	/// bare digits before the repository compare, otherwise a verified user can't be identified.
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
				new Mock<IChatbotIdentityRepository>().Object);
		}

		[TestCase("+12248304555", "12248304555")]
		[TestCase("12248304555", "12248304555")]
		[TestCase("(224) 830-4555", "2248304555")]
		[TestCase("224.830.4555", "2248304555")]
		[TestCase(" +1 224 830 4555 ", "12248304555")]
		public async Task GetProfileByMobileNumberAsync_strips_formatting_before_hitting_the_repository(string inbound, string expected)
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync(expected)).ReturnsAsync(profile);

			var result = await _service.GetProfileByMobileNumberAsync(inbound);

			result.Should().BeSameAs(profile);
			_repository.Verify(x => x.GetProfileByMobileNumberAsync(expected), Times.Once);
		}

		[Test]
		public async Task GetProfileByMobileNumberAsync_retries_without_the_us_country_code()
		{
			var profile = new UserProfile { UserId = "user-1" };
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("12248304555")).ReturnsAsync((UserProfile)null);
			_repository.Setup(x => x.GetProfileByMobileNumberAsync("2248304555")).ReturnsAsync(profile);

			var result = await _service.GetProfileByMobileNumberAsync("+12248304555");

			result.Should().BeSameAs(profile);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
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
			_repository.Setup(x => x.GetProfileByHomeNumberAsync("12248304555")).ReturnsAsync(profile);

			var result = await _service.GetProfileByHomeNumberAsync("+1 (224) 830-4555");

			result.Should().BeSameAs(profile);
			_repository.Verify(x => x.GetProfileByMobileNumberAsync(It.IsAny<string>()), Times.Never);
		}

		[TestCase(null)]
		[TestCase("")]
		public async Task GetProfileByHomeNumberAsync_never_matches_on_a_blank_number(string inbound)
		{
			var result = await _service.GetProfileByHomeNumberAsync(inbound);

			result.Should().BeNull();
			_repository.Verify(x => x.GetProfileByHomeNumberAsync(It.IsAny<string>()), Times.Never);
		}
	}
}
