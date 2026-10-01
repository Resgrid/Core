using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// A validated grant must also belong to the caller presenting it (plan section 8.3). Version 1 grants keep their
	/// user-only binding; version 2 grants must match the validated session exactly and fail closed without one.
	/// </summary>
	[TestFixture]
	public class ProtectedGrantBindingTests
	{
		private static readonly DateTime VerifiedAt = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

		private static ProtectedDataGrant VersionTwo(Action<ProtectedDataGrant> edit = null)
		{
			var grant = new ProtectedDataGrant
			{
				Version = 2,
				UserId = "user-1",
				DepartmentId = 42,
				SessionId = "session-9",
				ClientApp = (int)UserSessionClientApplication.Responder,
				AuthenticationGeneration = 4,
				MfaMethod = ProtectedDataGrantMfaMethods.Totp,
				MfaAtUtc = VerifiedAt,
				IssuedAtUtc = VerifiedAt.AddSeconds(5),
				ExpiresOnUtc = VerifiedAt.AddMinutes(15)
			};
			edit?.Invoke(grant);
			return grant;
		}

		private static ProtectedGrantSessionContext Session(Action<ProtectedGrantSessionContextBuilder> edit = null)
		{
			var builder = new ProtectedGrantSessionContextBuilder();
			edit?.Invoke(builder);
			return new ProtectedGrantSessionContext
			{
				SessionId = builder.SessionId,
				ClientApplication = builder.ClientApplication,
				AuthenticationGeneration = builder.AuthenticationGeneration,
				SessionLockVersion = builder.SessionLockVersion
			};
		}

		public sealed class ProtectedGrantSessionContextBuilder
		{
			public string SessionId = "session-9";
			public int ClientApplication = (int)UserSessionClientApplication.Responder;
			public long AuthenticationGeneration = 4;
			public long? SessionLockVersion;
		}

		[Test]
		public void A_version_two_grant_matching_the_validated_session_is_bound()
		{
			ProtectedGrantBinding.Check(VersionTwo(), "user-1", Session(), 15).Should().Be(ProtectedGrantBindingOutcome.Bound);
			ProtectedGrantBinding.Check(VersionTwo(), "USER-1", Session(), 15).Should().Be(ProtectedGrantBindingOutcome.Bound);
		}

		[Test]
		public void A_version_one_grant_keeps_its_user_only_binding()
		{
			var legacy = new ProtectedDataGrant { Version = 1, UserId = "user-1" };

			ProtectedGrantBinding.Check(legacy, "user-1", null).Should().Be(ProtectedGrantBindingOutcome.Bound);
			ProtectedGrantBinding.Check(legacy, "user-2", Session()).Should().Be(ProtectedGrantBindingOutcome.UserMismatch);
			ProtectedGrantBinding.Check(legacy, null, Session()).Should().Be(ProtectedGrantBindingOutcome.UserMismatch);
		}

		[Test]
		public void Without_a_validated_session_a_version_two_grant_is_refused()
		{
			ProtectedGrantBinding.Check(VersionTwo(), "user-1", null).Should().Be(ProtectedGrantBindingOutcome.SessionMismatch);
		}

		private static readonly (string Name, Action<ProtectedGrantSessionContextBuilder> Edit, ProtectedGrantBindingOutcome Expected)[] Mismatches =
		{
			("another session of the same user", s => s.SessionId = "session-10", ProtectedGrantBindingOutcome.SessionMismatch),
			("the Unit app on the same session id", s => s.ClientApplication = (int)UserSessionClientApplication.Unit, ProtectedGrantBindingOutcome.ClientMismatch),
			("a password change since issuance", s => s.AuthenticationGeneration = 5, ProtectedGrantBindingOutcome.GenerationMismatch),
			("a shared session locked since issuance", s => s.SessionLockVersion = 1, ProtectedGrantBindingOutcome.SessionLocked)
		};

		[TestCaseSource(nameof(Mismatches))]
		public void A_version_two_grant_never_moves_to_another_session_client_or_generation(
			(string Name, Action<ProtectedGrantSessionContextBuilder> Edit, ProtectedGrantBindingOutcome Expected) mismatch)
		{
			ProtectedGrantBinding.Check(VersionTwo(), "user-1", Session(mismatch.Edit)).Should().Be(mismatch.Expected, mismatch.Name);
		}

		[Test]
		public void A_grant_for_a_locked_version_needs_that_exact_version()
		{
			var grant = VersionTwo(g => g.SessionLockVersion = 3);

			ProtectedGrantBinding.Check(grant, "user-1", Session(s => s.SessionLockVersion = 3)).Should().Be(ProtectedGrantBindingOutcome.Bound);
			ProtectedGrantBinding.Check(grant, "user-1", Session(s => s.SessionLockVersion = 4)).Should().Be(ProtectedGrantBindingOutcome.SessionLocked);
			ProtectedGrantBinding.Check(grant, "user-1", Session()).Should().Be(ProtectedGrantBindingOutcome.SessionLocked);
		}

		[TestCase(ProtectedDataGrantMfaMethods.Passkey)]
		[TestCase(ProtectedDataGrantMfaMethods.PasskeyApproval)]
		[TestCase(ProtectedDataGrantMfaMethods.Federated)]
		public void Methods_whose_revocation_cannot_be_checked_yet_are_refused(string method)
		{
			ProtectedGrantBinding.Check(VersionTwo(g => g.MfaMethod = method), "user-1", Session())
				.Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable);
		}

		[Test]
		public void An_exempt_grant_is_bound_by_session_like_any_other()
		{
			var exempt = VersionTwo(g =>
			{
				g.StepUpExempt = true;
				g.MfaMethod = ProtectedDataGrantMfaMethods.None;
				g.MfaAtUtc = default;
			});

			ProtectedGrantBinding.Check(exempt, "user-1", Session(), 15).Should().Be(ProtectedGrantBindingOutcome.Bound);
			ProtectedGrantBinding.Check(exempt, "user-1", Session(s => s.SessionId = "other")).Should().Be(ProtectedGrantBindingOutcome.SessionMismatch);
		}

		[Test]
		public void A_shortened_department_window_ends_older_grants()
		{
			// Issued under a 15-minute window, the department has since moved to 5 minutes.
			ProtectedGrantBinding.Check(VersionTwo(), "user-1", Session(), 5).Should().Be(ProtectedGrantBindingOutcome.WindowExceeded);
			ProtectedGrantBinding.Check(VersionTwo(), "user-1", Session(), 15).Should().Be(ProtectedGrantBindingOutcome.Bound);
			ProtectedGrantBinding.Check(VersionTwo(), "user-1", Session()).Should().Be(ProtectedGrantBindingOutcome.Bound, "no policy, no window check");
		}

		[Test]
		public void The_effective_window_matches_the_issuers()
		{
			ProtectedGrantBinding.EffectiveWindowMinutes(0).Should().Be(Resgrid.Config.DataProtectionConfig.StepUpWindowDefaultMinutes);
			ProtectedGrantBinding.EffectiveWindowMinutes(100000).Should().Be(Resgrid.Config.DataProtectionConfig.StepUpMaximumMinutes);
			ProtectedGrantBinding.EffectiveWindowMinutes(20).Should().Be(20);
		}

		[Test]
		public void The_caller_session_comes_only_from_a_context_for_the_same_attended_user()
		{
			var session = Session();

			ProtectedGrantBinding.SessionFor(new FixedProtectedGrantContext("g", false, "user-1", session), "USER-1").Should().BeSameAs(session);
			ProtectedGrantBinding.SessionFor(new FixedProtectedGrantContext("g", false, "user-2", session), "user-1").Should().BeNull();
			ProtectedGrantBinding.SessionFor(new FixedProtectedGrantContext("g", true, "user-1", session), "user-1").Should().BeNull();
			ProtectedGrantBinding.SessionFor(null, "user-1").Should().BeNull();
		}

		[TestCase(ProtectedGrantBindingOutcome.Bound, null)]
		[TestCase(ProtectedGrantBindingOutcome.UserMismatch, "protected_access_denied")]
		[TestCase(ProtectedGrantBindingOutcome.SessionMismatch, "grant_session_mismatch")]
		[TestCase(ProtectedGrantBindingOutcome.ClientMismatch, "grant_client_mismatch")]
		[TestCase(ProtectedGrantBindingOutcome.GenerationMismatch, "grant_revoked")]
		[TestCase(ProtectedGrantBindingOutcome.SessionLocked, "grant_session_locked")]
		[TestCase(ProtectedGrantBindingOutcome.MethodUnverifiable, "grant_revoked")]
		[TestCase(ProtectedGrantBindingOutcome.WindowExceeded, "grant_revoked")]
		public void Binding_failures_use_the_shared_error_vocabulary(ProtectedGrantBindingOutcome outcome, string code)
		{
			ProtectedGrantBinding.ErrorCode(outcome).Should().Be(code);
		}

		[Test]
		public void An_unsupported_version_has_its_own_code()
		{
			ProtectedGrantBinding.ErrorCode(ProtectedDataGrantValidationOutcome.VersionUnsupported).Should().Be("grant_version_unsupported");
		}
	}
}
