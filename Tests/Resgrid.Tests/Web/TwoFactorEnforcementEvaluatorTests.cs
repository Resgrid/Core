using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.TwoFactor;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// Unit tests for <see cref="TwoFactorEnforcementEvaluator"/>.
	/// No mocks, no database, no HTTP — pure value-based assertions.
	/// </summary>
	namespace TwoFactorEnforcementEvaluatorTests
	{
		// ── shared fixtures ──────────────────────────────────────────────────────────

		/// <summary>Fixed "now" used across all tests so window calculations are deterministic.</summary>
		internal static class Clock
		{
			internal static readonly DateTime Now = new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc);
		}

		internal static class Contexts
		{
			internal const int WindowMinutes = 15;

			/// <summary>Returns a context with no 2FA and scope 0 — the baseline no-op case.</summary>
			internal static TwoFactorEnforcementContext NoTwoFaNoScope(
				bool isAdmin = false, bool isGroupAdmin = false) =>
				new TwoFactorEnforcementContext(
					UserHas2FaEnabled: false,
					DepartmentScope: 0,
					IsAdminOrManagingUser: isAdmin,
					IsGroupAdmin: isGroupAdmin,
					LastStepUpVerifiedAtUtc: null,
					StepUpWindowMinutes: WindowMinutes);

			internal static TwoFactorEnforcementContext Build(
				bool userHas2Fa,
				int scope,
				bool isAdmin = false,
				bool isGroupAdmin = false,
				DateTime? lastVerified = null) =>
				new TwoFactorEnforcementContext(
					UserHas2FaEnabled: userHas2Fa,
					DepartmentScope: scope,
					IsAdminOrManagingUser: isAdmin,
					IsGroupAdmin: isGroupAdmin,
					LastStepUpVerifiedAtUtc: lastVerified,
					StepUpWindowMinutes: WindowMinutes);
		}

		// ── Scope 0 ──────────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_scope_is_0
		{
			[Test]
			public void and_user_has_no_2fa_should_not_require_anything()
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: 0, isAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_has_2fa_but_no_step_up_proof_should_require_step_up()
			{
				var ctx = Contexts.Build(userHas2Fa: true, scope: 0, lastVerified: null);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}

			[Test]
			public void and_user_has_2fa_with_valid_step_up_proof_should_pass_through()
			{
				var recentVerification = Clock.Now.AddMinutes(-5);
				var ctx = Contexts.Build(userHas2Fa: true, scope: 0, lastVerified: recentVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_has_2fa_with_expired_step_up_proof_should_require_step_up()
			{
				var expiredVerification = Clock.Now.AddMinutes(-(Contexts.WindowMinutes + 1));
				var ctx = Contexts.Build(userHas2Fa: true, scope: 0, lastVerified: expiredVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}
		}

		// ── Scope 1 ──────────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_scope_is_1
		{
			[Test]
			public void and_user_is_not_admin_and_has_no_2fa_should_not_require_anything()
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: 1, isAdmin: false, isGroupAdmin: false);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_is_not_admin_but_has_2fa_should_require_step_up_when_no_proof()
			{
				// Voluntarily enrolled users always require step-up regardless of scope
				var ctx = Contexts.Build(userHas2Fa: true, scope: 1, isAdmin: false, isGroupAdmin: false, lastVerified: null);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}

			[Test]
			public void and_user_is_admin_with_no_2fa_should_require_enrollment()
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: 1, isAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.EnrollmentRequired);
			}

			[Test]
			public void and_user_is_managing_user_with_no_2fa_should_require_enrollment()
			{
				// IsAdminOrManagingUser covers the managing user — same flag
				var ctx = Contexts.Build(userHas2Fa: false, scope: 1, isAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.EnrollmentRequired);
			}

			[Test]
			public void and_user_is_admin_with_2fa_but_no_proof_should_require_step_up()
			{
				var ctx = Contexts.Build(userHas2Fa: true, scope: 1, isAdmin: true, lastVerified: null);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}

			[Test]
			public void and_user_is_admin_with_2fa_and_valid_proof_should_pass_through()
			{
				var recentVerification = Clock.Now.AddMinutes(-10);
				var ctx = Contexts.Build(userHas2Fa: true, scope: 1, isAdmin: true, lastVerified: recentVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_is_admin_with_2fa_and_proof_exactly_at_boundary_should_pass_through()
			{
				// Proof timestamp exactly at the edge of the window must still be accepted
				var boundaryVerification = Clock.Now.AddMinutes(-Contexts.WindowMinutes);
				var ctx = Contexts.Build(userHas2Fa: true, scope: 1, isAdmin: true, lastVerified: boundaryVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_is_admin_with_2fa_and_expired_proof_should_require_step_up()
			{
				var expiredVerification = Clock.Now.AddMinutes(-(Contexts.WindowMinutes + 1));
				var ctx = Contexts.Build(userHas2Fa: true, scope: 1, isAdmin: true, lastVerified: expiredVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}

			[Test]
			public void and_user_is_only_group_admin_with_no_2fa_should_not_require_anything()
			{
				// Scope 1 does not cover group admins
				var ctx = Contexts.Build(userHas2Fa: false, scope: 1, isAdmin: false, isGroupAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}
		}

		// ── Scope 2 ──────────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_scope_is_2
		{
			[Test]
			public void and_user_is_not_in_any_admin_role_and_has_no_2fa_should_not_require_anything()
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: 2, isAdmin: false, isGroupAdmin: false);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_is_dept_admin_with_no_2fa_should_require_enrollment()
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: 2, isAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.EnrollmentRequired);
			}

			[Test]
			public void and_user_is_group_admin_with_no_2fa_should_require_enrollment()
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: 2, isAdmin: false, isGroupAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.EnrollmentRequired);
			}

			[Test]
			public void and_user_is_group_admin_with_2fa_but_no_proof_should_require_step_up()
			{
				var ctx = Contexts.Build(userHas2Fa: true, scope: 2, isAdmin: false, isGroupAdmin: true, lastVerified: null);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}

			[Test]
			public void and_user_is_group_admin_with_2fa_and_valid_proof_should_pass_through()
			{
				var recentVerification = Clock.Now.AddMinutes(-7);
				var ctx = Contexts.Build(userHas2Fa: true, scope: 2, isAdmin: false, isGroupAdmin: true, lastVerified: recentVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_is_group_admin_with_2fa_and_expired_proof_should_require_step_up()
			{
				var expiredVerification = Clock.Now.AddMinutes(-(Contexts.WindowMinutes + 1));
				var ctx = Contexts.Build(userHas2Fa: true, scope: 2, isAdmin: false, isGroupAdmin: true, lastVerified: expiredVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired);
			}

			[Test]
			public void and_user_is_dept_admin_with_2fa_and_valid_proof_should_pass_through()
			{
				var recentVerification = Clock.Now.AddMinutes(-1);
				var ctx = Contexts.Build(userHas2Fa: true, scope: 2, isAdmin: true, lastVerified: recentVerification);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired);
			}

			[Test]
			public void and_user_is_group_admin_only_would_not_be_covered_by_scope_1()
			{
				// Confirm scope 1 does NOT cover group admins (cross-scope boundary test)
				var ctx = Contexts.Build(userHas2Fa: false, scope: 1, isAdmin: false, isGroupAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired,
					because: "scope 1 only covers dept admins and managing user, not group admins");
			}
		}

		// ── Unrecognised scope ────────────────────────────────────────────────────────

		[TestFixture]
		public class when_scope_is_unrecognised
		{
			[TestCase(3)]
			[TestCase(99)]
			[TestCase(-1)]
			public void and_user_has_no_2fa_should_not_require_anything(int scope)
			{
				var ctx = Contexts.Build(userHas2Fa: false, scope: scope, isAdmin: true, isGroupAdmin: true);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.NotRequired,
					because: "unrecognised scope values should not mandate enrollment");
			}

			[TestCase(3)]
			[TestCase(99)]
			public void and_user_has_2fa_should_still_require_step_up(int scope)
			{
				// Voluntarily enrolled users always require step-up regardless of scope
				var ctx = Contexts.Build(userHas2Fa: true, scope: scope, lastVerified: null);

				var result = TwoFactorEnforcementEvaluator.Evaluate(ctx, Clock.Now);

				result.Outcome.Should().Be(TwoFactorEnforcementOutcome.StepUpRequired,
					because: "a user who opted into 2FA must always present a step-up proof");
			}
		}
	}
}

