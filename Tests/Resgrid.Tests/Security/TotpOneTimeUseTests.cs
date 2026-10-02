using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Stores;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	[TestFixture]
	public class TotpOneTimeUseTests
	{
		// RFC 6238 appendix B SHA-1 seed "12345678901234567890", base32-encoded.
		private const string Rfc6238Key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
		private const string UserId = "3A1F6C0E-8E0B-4C62-9C2D-0B7A1F3E5D11";
		private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static string CodeAt(DateTime utc, int stepOffset = 0)
			=> TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(Rfc6238Key), TotpCalculator.CurrentTimeStep(utc) + stepOffset).ToString("D6");

		[TestCase(59L, "287082")]
		[TestCase(1111111109L, "081804")]
		[TestCase(1111111111L, "050471")]
		[TestCase(1234567890L, "005924")]
		[TestCase(2000000000L, "279037")]
		[TestCase(20000000000L, "353130")]
		public void Computes_the_rfc6238_sha1_test_vectors(long unixSeconds, string expected)
		{
			var step = TotpCalculator.CurrentTimeStep(Epoch.AddSeconds(unixSeconds));

			TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(Rfc6238Key), step).ToString("D6").Should().Be(expected);
		}

		[Test]
		public void Base32_decodes_the_rfc_seed_and_ignores_case_spaces_and_padding()
		{
			Encoding.ASCII.GetString(TotpCalculator.Base32Decode(Rfc6238Key)).Should().Be("12345678901234567890");
			TotpCalculator.Base32Decode("gezd gnbv gy3t qojq gezd gnbv gy3t qojq====").Should().Equal(TotpCalculator.Base32Decode(Rfc6238Key));
			FluentActions.Invoking(() => TotpCalculator.Base32Decode("NOT*BASE32")).Should().Throw<FormatException>();
		}

		[TestCase(-2, true)]
		[TestCase(-1, true)]
		[TestCase(0, true)]
		[TestCase(1, true)]
		[TestCase(2, true)]
		[TestCase(-3, false)]
		[TestCase(3, false)]
		public void Accepts_the_same_two_step_window_as_identity(int offset, bool accepted)
		{
			var now = Epoch.AddSeconds(1234567890);

			var match = TotpCalculator.FindMatchingTimeStep(Rfc6238Key, CodeAt(now, offset), now);

			if (accepted)
				match.Should().Be(TotpCalculator.CurrentTimeStep(now) + offset);
			else
				match.Should().BeNull();
		}

		[Test]
		public void Treats_codes_as_integers_like_identity_and_rejects_malformed_input()
		{
			var now = Epoch.AddSeconds(1111111109); // code 081804
			TotpCalculator.FindMatchingTimeStep(Rfc6238Key, "081804", now).Should().NotBeNull();
			TotpCalculator.FindMatchingTimeStep(Rfc6238Key, "81804", now).Should().NotBeNull();
			TotpCalculator.FindMatchingTimeStep(Rfc6238Key, " 081-804 ", now).Should().NotBeNull();

			TotpCalculator.FindMatchingTimeStep(Rfc6238Key, "08180A", now).Should().BeNull();
			TotpCalculator.FindMatchingTimeStep(Rfc6238Key, "", now).Should().BeNull();
			TotpCalculator.FindMatchingTimeStep(Rfc6238Key, "123456789", now).Should().BeNull();
			TotpCalculator.FindMatchingTimeStep("not*base32", "081804", now).Should().BeNull();
			TotpCalculator.FindMatchingTimeStep(null, "081804", now).Should().BeNull();
		}

		[Test]
		public async Task A_code_is_accepted_once_and_its_replay_is_rejected()
		{
			var state = new InMemoryUserMfaStateRepository();
			var now = DateTime.UtcNow;
			var code = CodeAt(now);

			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, code, now)).Should().BeTrue();
			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, code, now)).Should().BeFalse();
			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, code, now.AddSeconds(20))).Should().BeFalse();
		}

		[Test]
		public async Task A_later_step_is_accepted_but_an_earlier_one_never_is_after_it()
		{
			var state = new InMemoryUserMfaStateRepository();
			var now = DateTime.UtcNow;

			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, CodeAt(now, 1), now)).Should().BeTrue();
			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, CodeAt(now, 0), now)).Should().BeFalse();
			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, CodeAt(now, 2), now)).Should().BeTrue();
		}

		[Test]
		public async Task Steps_are_tracked_per_user()
		{
			var state = new InMemoryUserMfaStateRepository();
			var now = DateTime.UtcNow;
			var code = CodeAt(now);

			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, code, now)).Should().BeTrue();
			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, "another-user", Rfc6238Key, code, now)).Should().BeTrue();
		}

		[Test]
		public async Task A_wrong_code_never_consumes_a_step()
		{
			var state = new InMemoryUserMfaStateRepository();
			var now = DateTime.UtcNow;
			var wrong = ((int.Parse(CodeAt(now)) + 1) % 1_000_000).ToString("D6");

			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, wrong, now)).Should().BeFalse();
			state.ConsumeCalls.Should().Be(0);
			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, CodeAt(now), now)).Should().BeTrue();
		}

		[Test]
		public async Task A_storage_fault_rejects_the_code()
		{
			var state = new InMemoryUserMfaStateRepository { ThrowOnConsume = new InvalidOperationException("database down") };
			var now = DateTime.UtcNow;

			(await ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(state, UserId, Rfc6238Key, CodeAt(now), now)).Should().BeFalse();
		}

		[Test]
		public async Task Identity_verification_goes_through_the_one_time_provider()
		{
			var state = new InMemoryUserMfaStateRepository();
			var user = new IdentityUser { Id = UserId };
			var manager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			manager.Setup(m => m.GetAuthenticatorKeyAsync(user)).ReturnsAsync(Rfc6238Key);
			var provider = new ResgridAuthenticatorTokenProvider(state);
			var code = CodeAt(DateTime.UtcNow);

			(await provider.CanGenerateTwoFactorTokenAsync(manager.Object, user)).Should().BeTrue();
			(await provider.GenerateAsync("TwoFactor", manager.Object, user)).Should().BeEmpty();
			(await provider.ValidateAsync("TwoFactor", code, manager.Object, user)).Should().BeTrue();
			(await provider.ValidateAsync("TwoFactor", code, manager.Object, user)).Should().BeFalse("the step was already used");
		}

		[Test]
		public async Task A_user_without_an_authenticator_key_cannot_use_the_provider()
		{
			var user = new IdentityUser { Id = UserId };
			var manager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			manager.Setup(m => m.GetAuthenticatorKeyAsync(user)).ReturnsAsync((string)null);
			var provider = new ResgridAuthenticatorTokenProvider(Mock.Of<IUserMfaStateRepository>());

			(await provider.CanGenerateTwoFactorTokenAsync(manager.Object, user)).Should().BeFalse();
			(await provider.ValidateAsync("TwoFactor", "123456", manager.Object, user)).Should().BeFalse();
		}
	}
}
