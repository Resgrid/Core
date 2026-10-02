using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>Server-side MFA evidence (plan section 5.3): what may be recorded, and what counts as fresh.</summary>
	[TestFixture, NonParallelizable]
	public class MfaEvidenceServiceTests
	{
		private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

		private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }

		private Mock<IUserSessionMfaEvidenceRepository> _repository;
		private MfaEvidenceService _service;
		private InMemoryUserMfaStateRepository _mfaState;
		private InMemoryMfaActivityRepository _activity;
		private int _retention;

		[SetUp]
		public void SetUp()
		{
			_retention = TwoFactorConfig.MfaEvidenceRetentionHours;
			_repository = new Mock<IUserSessionMfaEvidenceRepository>();
			_mfaState = new InMemoryUserMfaStateRepository();
			_activity = new InMemoryMfaActivityRepository();
			_service = new MfaEvidenceService(_repository.Object, _mfaState, Mock.Of<IUserPasskeyRepository>(), Mock.Of<IUserSessionsRepository>(), _activity, new Clock());
		}

		[TearDown]
		public void TearDown() => TwoFactorConfig.MfaEvidenceRetentionHours = _retention;

		[Test]
		public async Task Evidence_is_stored_for_its_session_with_the_retention_expiry()
		{
			TwoFactorConfig.MfaEvidenceRetentionHours = 24;
			MfaEvidence stored = null;
			_repository.Setup(r => r.InsertAsync(It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()))
				.Callback<MfaEvidence, CancellationToken>((e, _) => stored = e)
				.Returns(Task.CompletedTask);

			await _service.RecordAsync("user-1", "sid:abc", UserSessionClientApplication.Web, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.Totp, MfaEvidencePurpose.Login, Now, 7, departmentId: 42);

			stored.UserId.Should().Be("user-1");
			stored.SessionKey.Should().Be("sid:abc");
			stored.Kind.Should().Be((int)MfaEvidenceKind.SecondFactor);
			stored.Method.Should().Be((int)MfaEvidenceMethod.Totp);
			stored.AuthenticationGeneration.Should().Be(7);
			stored.DepartmentId.Should().Be(42);
			stored.ExpiresOnUtc.Should().Be(Now.AddHours(24));
		}

		[TestCase(MfaEvidenceKind.FirstFactor)]
		[TestCase(MfaEvidenceKind.SecondFactor)]
		public async Task A_recovery_code_is_never_recorded_as_a_normal_factor(MfaEvidenceKind kind)
		{
			var record = () => _service.RecordAsync("user-1", "sid:abc", UserSessionClientApplication.Web, kind,
				MfaEvidenceMethod.RecoveryCode, MfaEvidencePurpose.Login, Now, 7);

			await record.Should().ThrowAsync<ArgumentException>();
			_repository.Verify(r => r.InsertAsync(It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(null, "sid:abc")]
		[TestCase("user-1", null)]
		[TestCase("user-1", " ")]
		public async Task Evidence_needs_a_user_and_a_session(string userId, string sessionKey)
		{
			var record = () => _service.RecordAsync(userId, sessionKey, UserSessionClientApplication.Web, MfaEvidenceKind.FirstFactor,
				MfaEvidenceMethod.Password, MfaEvidencePurpose.Login, Now, 7);

			await record.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Freshness_is_read_for_the_current_generation_only()
		{
			_repository.Setup(r => r.GetLatestAsync("user-1", "sid:abc", MfaEvidenceKind.FirstFactor, 7, Now, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaEvidence { VerifiedOnUtc = Now.AddMinutes(-2) });

			(await _service.HasFreshFirstFactorAsync("user-1", "sid:abc", 7, TimeSpan.FromMinutes(5), Now)).Should().BeTrue();
			(await _service.HasFreshFirstFactorAsync("user-1", "sid:abc", 8, TimeSpan.FromMinutes(5), Now))
				.Should().BeFalse("evidence from before a password change or revocation never counts");
		}

		[Test]
		public async Task No_session_means_no_evidence()
		{
			(await _service.HasFreshFirstFactorAsync("user-1", null, 7, TimeSpan.FromMinutes(5), Now)).Should().BeFalse();
			_repository.Verify(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MfaEvidenceKind>(), It.IsAny<long>(),
				It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(0, true)]
		[TestCase(-4 * 60, true)]
		[TestCase(-5 * 60, true)]
		[TestCase(-5 * 60 - 1, false)]
		[TestCase(20, true)]
		[TestCase(31, false)]
		public void Fresh_means_recent_and_not_from_the_future(int offsetSeconds, bool fresh)
		{
			var evidence = new MfaEvidence { VerifiedOnUtc = Now.AddSeconds(offsetSeconds) };

			MfaEvidenceService.IsFresh(evidence, TimeSpan.FromMinutes(5), Now).Should().Be(fresh);
		}

		[Test]
		public void Missing_evidence_is_never_fresh()
		{
			MfaEvidenceService.IsFresh(null, TimeSpan.FromDays(1), Now).Should().BeFalse();
		}

		[Test]
		public async Task Revocation_uses_the_current_time()
		{
			await _service.RevokeForUserAsync("user-1");

			_repository.Verify(r => r.RevokeForUserAsync("user-1", Now, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
