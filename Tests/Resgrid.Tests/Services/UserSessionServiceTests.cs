using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UserSessionServiceTests
	{
		private Mock<IUserSessionsRepository> _sessions;
		private Mock<IIdentityUserRepository> _users;
		private Mock<IIdentityRepository> _identity;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentSsoService> _departmentSso;
		private UserSessionService _service;

		[SetUp]
		public void SetUp()
		{
			_sessions = new Mock<IUserSessionsRepository>();
			_users = new Mock<IIdentityUserRepository>();
			_identity = new Mock<IIdentityRepository>();
			_departments = new Mock<IDepartmentsService>();
			_departmentSso = new Mock<IDepartmentSsoService>();
			_service = new UserSessionService(_sessions.Object, _users.Object, _identity.Object,
				_departments.Object, _departmentSso.Object, new ClientSessionMetadataParser(), Mock.Of<IIpLocationProvider>());
			SessionSecurityConfig.LegacyAdoptionEnabled = true;
			SessionSecurityConfig.RequireSessionClaimForCredentialsIssuedAfterUtc = string.Empty;
			SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = string.Empty;
		}

		[Test]
		public async Task rejects_credentials_at_or_before_the_account_cutoff()
		{
			var cutoff = DateTime.UtcNow;
			_users.Setup(x => x.GetByIdAsync("user-1")).ReturnsAsync(new IdentityUser
			{
				Id = "user-1",
				CredentialsValidAfterUtc = cutoff
			});

			var result = await _service.ValidateAsync(new SessionPrincipalContext
			{
				UserId = "user-1",
				CredentialIssuedOn = cutoff
			});

			Assert.That(result.IsValid, Is.False);
			Assert.That(result.FailureCode, Is.EqualTo("credential_cutoff"));
		}

		[Test]
		public async Task accepts_a_pre_feature_credential_for_lazy_adoption()
		{
			_users.Setup(x => x.GetByIdAsync("user-1")).ReturnsAsync(new IdentityUser
			{
				Id = "user-1",
				AuthenticationGeneration = 0
			});

			var result = await _service.ValidateAsync(new SessionPrincipalContext
			{
				UserId = "user-1",
				CredentialIssuedOn = DateTime.UtcNow.AddDays(-1)
			});

			Assert.That(result.IsValid, Is.True);
			Assert.That(result.CanAdoptLegacy, Is.True);
		}

		[Test]
		public async Task rejects_a_revoked_individual_session_immediately()
		{
			_users.Setup(x => x.GetByIdAsync("user-1")).ReturnsAsync(new IdentityUser
			{
				Id = "user-1",
				AuthenticationGeneration = 4
			});
			_sessions.Setup(x => x.GetByIdAsync((object)"session-1")).ReturnsAsync(new UserSession
			{
				UserSessionId = "session-1",
				UserId = "user-1",
				AuthenticationGeneration = 4,
				State = (int)UserSessionState.Revoked,
				ExpiresOn = DateTime.UtcNow.AddHours(1)
			});

			var result = await _service.ValidateAsync(new SessionPrincipalContext
			{
				UserId = "user-1",
				SessionId = "session-1",
				AuthenticationGeneration = 4,
				CredentialIssuedOn = DateTime.UtcNow.AddMinutes(-1)
			});

			Assert.That(result.IsValid, Is.False);
			Assert.That(result.FailureCode, Is.EqualTo("session_revoked"));
		}

		[Test]
		public async Task revoke_all_rotates_account_state_and_removes_oidc_credentials()
		{
			var user = new IdentityUser {Id = "user-1", AuthenticationGeneration = 7, SecurityStamp = "old"};
			var cutoff = DateTime.UtcNow;
			_users.Setup(x => x.GetByIdAsync("user-1")).ReturnsAsync(user);
			_users.Setup(x => x.UpdateAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_sessions.Setup(x => x.RevokeAllAsync("user-1", "admin-1", (int)UserSessionRevocationReason.PasswordReset,
				cutoff, It.IsAny<CancellationToken>())).ReturnsAsync(3);

			var result = await _service.RevokeAllAsync("admin-1", "user-1",
				UserSessionRevocationReason.PasswordReset, cutoff);

			Assert.That(user.AuthenticationGeneration, Is.EqualTo(8));
			Assert.That(user.CredentialsValidAfterUtc, Is.EqualTo(cutoff));
			Assert.That(user.SecurityStamp, Is.Not.EqualTo("old"));
			Assert.That(result.RevokedSessionCount, Is.EqualTo(3));
			_identity.Verify(x => x.CleanUpOIDCTokensByUserAsync("user-1"), Times.Once);
		}

		[Test]
		public async Task moving_a_session_requires_an_active_membership_and_owned_active_session()
		{
			_departments.Setup(x => x.GetDepartmentMemberAsync("user-1", 22, true))
				.ReturnsAsync(new DepartmentMember {UserId = "user-1", DepartmentId = 22});
			_sessions.Setup(x => x.UpdateDepartmentAsync("user-1", "session-1", 22,
				It.IsAny<CancellationToken>())).ReturnsAsync(1);

			var moved = await _service.MoveSessionToDepartmentAsync("user-1", "session-1", 22);

			Assert.That(moved, Is.True);
			_sessions.Verify(x => x.UpdateDepartmentAsync("user-1", "session-1", 22,
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void policy_gate_denies_a_new_session_at_the_department_limit()
		{
			var gate = DateTime.UtcNow.AddMinutes(-5);
			SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = gate.ToString("O");
			_departments.Setup(x => x.GetDepartmentMemberAsync("user-1", 22, true))
				.ReturnsAsync(new DepartmentMember {UserId = "user-1", DepartmentId = 22});
			_departmentSso.Setup(x => x.GetSecurityPolicyForDepartmentAsync(22, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy {DepartmentId = 22, MaxConcurrentSessions = 1});
			_sessions.Setup(x => x.GetActiveByUserAsync("user-1", It.IsAny<DateTime>()))
				.ReturnsAsync(new[]
				{
					new UserSession {UserId = "user-1", DepartmentId = 22, CreatedOn = gate.AddMinutes(1)}
				});

			Assert.ThrowsAsync<SessionCreationDeniedException>(() => _service.CreateSessionAsync(new SessionIssueContext
			{
				UserId = "user-1",
				DepartmentId = 22,
				ExpiresOn = DateTime.UtcNow.AddHours(1)
			}));
		}

		[Test]
		public void session_creation_denies_an_inactive_department_membership()
		{
			_departments.Setup(x => x.GetDepartmentMemberAsync("user-1", 22, true))
				.ReturnsAsync(new DepartmentMember {UserId = "user-1", DepartmentId = 22, IsDisabled = true});

			var exception = Assert.ThrowsAsync<SessionCreationDeniedException>(() =>
				_service.CreateSessionAsync(new SessionIssueContext
				{
					UserId = "user-1",
					DepartmentId = 22,
					ExpiresOn = DateTime.UtcNow.AddHours(1)
				}));

			Assert.That(exception.FailureCode, Is.EqualTo("membership_inactive"));
		}

		[Test]
		public async Task policy_gate_rejects_an_idle_managed_session()
		{
			var gate = DateTime.UtcNow.AddHours(-2);
			SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = gate.ToString("O");
			_users.Setup(x => x.GetByIdAsync("user-1")).ReturnsAsync(new IdentityUser
			{
				Id = "user-1",
				AuthenticationGeneration = 1
			});
			_sessions.Setup(x => x.GetByIdAsync((object)"session-1")).ReturnsAsync(new UserSession
			{
				UserSessionId = "session-1",
				UserId = "user-1",
				DepartmentId = 22,
				AuthenticationGeneration = 1,
				State = (int)UserSessionState.Active,
				CreatedOn = gate.AddMinutes(1),
				LastActiveOn = DateTime.UtcNow.AddMinutes(-31),
				ExpiresOn = DateTime.UtcNow.AddHours(1)
			});
			_departments.Setup(x => x.GetDepartmentMemberAsync("user-1", 22, true))
				.ReturnsAsync(new DepartmentMember {UserId = "user-1", DepartmentId = 22});
			_departmentSso.Setup(x => x.GetSecurityPolicyForDepartmentAsync(22, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy {DepartmentId = 22, SessionTimeoutMinutes = 30});

			var result = await _service.ValidateAsync(new SessionPrincipalContext
			{
				UserId = "user-1",
				SessionId = "session-1",
				AuthenticationGeneration = 1,
				DepartmentId = 22,
				CredentialIssuedOn = DateTime.UtcNow.AddMinutes(-40)
			});

			Assert.That(result.IsValid, Is.False);
			Assert.That(result.FailureCode, Is.EqualTo("session_idle_timeout"));
		}
	}
}
