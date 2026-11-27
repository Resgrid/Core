using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5 (owner decision 2026-11-26): the department step-up policy. Defaults keep today's behaviour, an unreadable policy fails
	/// closed, saving guards protected data for the managing member and keeps read-and-sign on its one switch, and evaluation counts only
	/// this session's fresh evidence of a kind the area allows, for this account generation and department.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class StepUpPolicyServiceTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionKey = "sid:unit-session";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = DateTime.UtcNow;
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private Mock<IDepartmentSettingsService> _settings;
		private Dictionary<DepartmentSettingTypes, string> _stored;
		private ProtocolLibrarySettings _library;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private Mock<IMfaPolicyService> _mfaPolicy;
		private Mock<IDepartmentDataProtectionService> _protection;
		private DepartmentDataProtectionPolicy _adpPolicy;
		private List<AuditEvent> _audits;

		[SetUp]
		public void SetUp()
		{
			_clock = new Clock();
			_stored = new Dictionary<DepartmentSettingTypes, string>();
			_library = new ProtocolLibrarySettings { DefaultReviewIntervalDays = 365, LegalHold = true };
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetSettingByTypeAsync(DepartmentId, It.IsAny<DepartmentSettingTypes>()))
				.ReturnsAsync((int d, DepartmentSettingTypes t) => _stored.TryGetValue(t, out var v) ? new DepartmentSetting { DepartmentId = d, SettingType = (int)t, Setting = v } : null);
			_settings.Setup(s => s.SaveOrUpdateSettingAsync(DepartmentId, It.IsAny<string>(), It.IsAny<DepartmentSettingTypes>(), It.IsAny<CancellationToken>()))
				.Callback((int _, string v, DepartmentSettingTypes t, CancellationToken _) => _stored[t] = v)
				.ReturnsAsync(new DepartmentSetting());
			_settings.Setup(s => s.GetProtocolLibrarySettingsAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => _stored.TryGetValue(DepartmentSettingTypes.ProtocolLibrarySettings, out var v) ? Library(v) : _library);
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_mfaPolicy = new Mock<IMfaPolicyService>();
			_mfaPolicy.Setup(p => p.IsEvidenceAcceptedAsync(It.IsAny<int?>(), It.IsAny<MfaMethodScope>(), It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_adpPolicy = new DepartmentDataProtectionPolicy { DepartmentId = DepartmentId, PolicyEpoch = 3, StepUpWindowMinutes = 20 };
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _adpPolicy);
			_audits = new List<AuditEvent>();
		}

		private StepUpPolicyService Service()
		{
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent a) => _audits.Add(a));
			var evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), new InMemoryUserPasskeyRepository(),
				new InMemoryUserSessionsRepository(), new InMemoryMfaActivityRepository(), _clock);
			return new StepUpPolicyService(_settings.Object, evidence, _mfaPolicy.Object, _protection.Object, events.Object, _clock);
		}

		/// <summary>As the real cached getter: an unreadable value reads as the defaults.</summary>
		private static ProtocolLibrarySettings Library(string value)
		{
			try
			{
				return ObjectSerialization.Deserialize<ProtocolLibrarySettings>(value) ?? new ProtocolLibrarySettings();
			}
			catch (Exception)
			{
				return new ProtocolLibrarySettings();
			}
		}

		private void Store(params StepUpAreaSetting[] areas) =>
			_stored[DepartmentSettingTypes.StepUpPolicy] = ObjectSerialization.Serialize(new DepartmentStepUpPolicy { Areas = areas.ToList() });

		private static StepUpSubject Subject(long generation = 4, string sessionKey = SessionKey, int departmentId = DepartmentId) => new()
		{
			UserId = UserId, DepartmentId = departmentId, SessionKey = sessionKey, AuthenticationGeneration = generation
		};

		private MfaEvidence Evidence(MfaEvidenceKind kind, MfaEvidenceMethod method, MfaEvidencePurpose purpose, DateTime verifiedOn, int? departmentId = DepartmentId,
			long generation = 4)
		{
			var row = new MfaEvidence
			{
				MfaEvidenceId = Guid.NewGuid().ToString(), UserId = UserId, SessionKey = SessionKey, ClientApplication = (int)UserSessionClientApplication.Unit,
				Kind = (int)kind, Method = (int)method, Purpose = (int)purpose, DepartmentId = departmentId, VerifiedOnUtc = verifiedOn,
				ExpiresOnUtc = verifiedOn.AddHours(12), AuthenticationGeneration = generation
			};
			_evidenceRows.Rows.Add(row);
			return row;
		}

		// ── Defaults and reading ───────────────────────────────────────────────────

		[Test]
		public async Task A_department_with_no_policy_keeps_todays_behaviour_in_every_area()
		{
			var rules = await Service().GetRulesAsync(DepartmentId);

			rules.Select(r => r.Area).Should().Equal(StepUpArea.Signatures, StepUpArea.Acknowledgments, StepUpArea.Approvals, StepUpArea.ProtectedData);
			rules.Should().OnlyContain(r => r.Methods == StepUpMethodClasses.Mfa, "a second factor only, until a department allows more");
			rules.Single(r => r.Area == StepUpArea.Signatures).Required.Should().BeTrue();
			rules.Single(r => r.Area == StepUpArea.Approvals).Required.Should().BeTrue();
			rules.Single(r => r.Area == StepUpArea.Acknowledgments).Required.Should().BeFalse("read-and-sign follows ReadAndSignRequiresStepUp, off by default");
			rules.Single(r => r.Area == StepUpArea.ProtectedData).Required.Should().BeTrue("the ADP exemptions decide per app, never this page");
			rules.Single(r => r.Area == StepUpArea.Signatures).WindowMinutes.Should().Be(Math.Max(1, Resgrid.Config.TwoFactorConfig.StepUpVerificationWindowMinutes));
			rules.Single(r => r.Area == StepUpArea.ProtectedData).WindowMinutes.Should().Be(20, "protected data uses the department's ADP grant window");
		}

		[Test]
		public async Task Read_and_sign_is_required_exactly_when_the_protocol_library_switch_is_on()
		{
			_library.ReadAndSignRequiresStepUp = true;
			(await Service().GetRuleAsync(DepartmentId, StepUpArea.Acknowledgments)).Required.Should().BeTrue();
		}

		[Test]
		public async Task Stored_choices_make_an_area_optional_and_add_alternatives_but_never_remove_the_second_factor()
		{
			Store(new StepUpAreaSetting { Area = (int)StepUpArea.Signatures, Optional = true, AlternativeMethods = (int)StepUpMethodClasses.Password },
				new StepUpAreaSetting { Area = (int)StepUpArea.Approvals, AlternativeMethods = 0xFF },
				new StepUpAreaSetting { Area = (int)StepUpArea.ProtectedData, Optional = true, AlternativeMethods = (int)StepUpMethodClasses.Sso },
				new StepUpAreaSetting { Area = 99, Optional = true });

			var rules = (await Service().GetRulesAsync(DepartmentId)).ToDictionary(r => r.Area);

			rules[StepUpArea.Signatures].Required.Should().BeFalse();
			rules[StepUpArea.Signatures].Methods.Should().Be(StepUpMethodClasses.Mfa | StepUpMethodClasses.Password);
			rules[StepUpArea.Approvals].Required.Should().BeTrue();
			rules[StepUpArea.Approvals].Methods.Should().Be(StepUpMethodClasses.Mfa | StepUpMethodClasses.Sso | StepUpMethodClasses.Password,
				"unknown bits are masked away");
			rules[StepUpArea.ProtectedData].Required.Should().BeTrue("an optional flag stored for protected data is ignored");
			rules[StepUpArea.ProtectedData].Methods.Should().Be(StepUpMethodClasses.Mfa | StepUpMethodClasses.Sso);
		}

		[Test]
		public async Task An_unreadable_policy_fails_closed_to_the_strict_defaults()
		{
			_stored[DepartmentSettingTypes.StepUpPolicy] = "not a protobuf payload";
			var rules = await Service().GetRulesAsync(DepartmentId);
			rules.Where(r => r.Area != StepUpArea.Acknowledgments).Should().OnlyContain(r => r.Required && r.Methods == StepUpMethodClasses.Mfa && r.FromFallback);

			_settings.Setup(s => s.GetSettingByTypeAsync(DepartmentId, DepartmentSettingTypes.StepUpPolicy)).ThrowsAsync(new InvalidOperationException("db down"));
			(await Service().GetRuleAsync(DepartmentId, StepUpArea.Signatures)).Should().Match<StepUpRule>(r => r.Required && r.Methods == StepUpMethodClasses.Mfa);
		}

		[Test]
		public void The_stored_policy_survives_serialization_including_false_and_zero_values()
		{
			var policy = new DepartmentStepUpPolicy
			{
				Areas = { new StepUpAreaSetting { Area = 1, Optional = true, AlternativeMethods = 6 }, new StepUpAreaSetting { Area = 3, Optional = false, AlternativeMethods = 0 } }
			};
			var back = ObjectSerialization.Deserialize<DepartmentStepUpPolicy>(ObjectSerialization.Serialize(policy)).Normalize();
			back.For(StepUpArea.Signatures).Should().BeEquivalentTo(new StepUpAreaSetting { Area = 1, Optional = true, AlternativeMethods = 6 });
			back.For(StepUpArea.Approvals).Optional.Should().BeFalse("required is the default a dropped protobuf member falls back to");
		}

		// ── Saving ─────────────────────────────────────────────────────────────────

		[Test]
		public async Task Saving_rejects_unknown_or_repeated_areas_and_writes_nothing()
		{
			var service = Service();
			(await service.SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = (StepUpArea)99 } }, UserId, true)).Should().Be(StepUpPolicySaveOutcome.InvalidRequest);
			(await service.SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.Signatures }, new StepUpRuleInput { Area = StepUpArea.Signatures } }, UserId, true))
				.Should().Be(StepUpPolicySaveOutcome.InvalidRequest);
			_stored.Should().BeEmpty();
		}

		[Test]
		public async Task Only_the_managing_member_may_change_how_protected_data_is_verified()
		{
			var outcome = await Service().SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.ProtectedData, Required = true, AllowPassword = true } },
				UserId, isManagingMember: false);

			outcome.Should().Be(StepUpPolicySaveOutcome.ManagingMemberRequired);
			_stored.Should().NotContainKey(DepartmentSettingTypes.StepUpPolicy);
			_protection.Verify(p => p.IncrementPolicyEpochAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_managing_member_changing_protected_data_methods_ends_every_grant_issued_under_the_old_rule()
		{
			var outcome = await Service().SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.ProtectedData, Required = true, AllowPassword = true } },
				UserId, isManagingMember: true);

			outcome.Should().Be(StepUpPolicySaveOutcome.Saved);
			(await Service().GetRuleAsync(DepartmentId, StepUpArea.ProtectedData)).Methods.Should().Be(StepUpMethodClasses.Mfa | StepUpMethodClasses.Password);
			_protection.Verify(p => p.IncrementPolicyEpochAsync(DepartmentId, UserId, It.IsAny<CancellationToken>()), Times.Once);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.StepUpPolicyChanged && a.DepartmentId == DepartmentId && a.UserId == UserId)
				.Which.After.Should().Contain("protected_data").And.Contain("password");
		}

		[Test]
		public async Task Signatures_and_approvals_can_be_made_optional_and_back_without_touching_protected_data()
		{
			var service = Service();
			(await service.SaveAsync(DepartmentId, new[]
			{
				new StepUpRuleInput { Area = StepUpArea.Signatures, Required = false, AllowSso = true },
				new StepUpRuleInput { Area = StepUpArea.Approvals, Required = true, AllowPassword = true }
			}, UserId, false)).Should().Be(StepUpPolicySaveOutcome.Saved);

			var rules = (await service.GetRulesAsync(DepartmentId)).ToDictionary(r => r.Area);
			rules[StepUpArea.Signatures].Should().Match<StepUpRule>(r => !r.Required && r.Methods == (StepUpMethodClasses.Mfa | StepUpMethodClasses.Sso));
			rules[StepUpArea.Approvals].Should().Match<StepUpRule>(r => r.Required && r.Methods == (StepUpMethodClasses.Mfa | StepUpMethodClasses.Password));
			_protection.Verify(p => p.IncrementPolicyEpochAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			(await service.SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.Signatures, Required = false, AllowSso = true } }, UserId, false))
				.Should().Be(StepUpPolicySaveOutcome.Unchanged);
			(await service.SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.Signatures, Required = true } }, UserId, false))
				.Should().Be(StepUpPolicySaveOutcome.Saved);
			(await service.GetRuleAsync(DepartmentId, StepUpArea.Signatures)).Should().Match<StepUpRule>(r => r.Required && r.Methods == StepUpMethodClasses.Mfa);
		}

		[Test]
		public async Task Read_and_sign_required_is_written_to_the_protocol_library_switch_keeping_its_other_settings()
		{
			_stored[DepartmentSettingTypes.ProtocolLibrarySettings] = ObjectSerialization.Serialize(_library);

			var outcome = await Service().SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.Acknowledgments, Required = true } }, UserId, false);

			outcome.Should().Be(StepUpPolicySaveOutcome.Saved);
			var library = ObjectSerialization.Deserialize<ProtocolLibrarySettings>(_stored[DepartmentSettingTypes.ProtocolLibrarySettings]);
			library.ReadAndSignRequiresStepUp.Should().BeTrue();
			library.DefaultReviewIntervalDays.Should().Be(365);
			library.LegalHold.Should().BeTrue();
		}

		[Test]
		public async Task An_unreadable_protocol_library_setting_is_never_overwritten_with_defaults()
		{
			_stored[DepartmentSettingTypes.ProtocolLibrarySettings] = "corrupt";

			var outcome = await Service().SaveAsync(DepartmentId, new[] { new StepUpRuleInput { Area = StepUpArea.Acknowledgments, Required = true } }, UserId, false);

			outcome.Should().Be(StepUpPolicySaveOutcome.Failed);
			_stored[DepartmentSettingTypes.ProtocolLibrarySettings].Should().Be("corrupt");
		}

		// ── Evaluation ─────────────────────────────────────────────────────────────

		[Test]
		public async Task An_optional_area_is_not_required_and_a_required_one_with_no_session_is_not_satisfied()
		{
			Store(new StepUpAreaSetting { Area = (int)StepUpArea.Signatures, Optional = true });
			(await Service().EvaluateAsync(Subject(), StepUpArea.Signatures)).Should().Match<StepUpEvaluation>(e => e.NotRequired && e.Allowed);
			(await Service().EvaluateAsync(Subject(sessionKey: null), StepUpArea.Approvals)).Should().Match<StepUpEvaluation>(e => !e.Allowed);
		}

		[Test]
		public async Task A_fresh_accepted_second_factor_satisfies_the_area_until_its_window_ends()
		{
			var verified = _clock.Now.AddMinutes(-3);
			Evidence(MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, verified);

			var evaluation = await Service().EvaluateAsync(Subject(), StepUpArea.Approvals);

			evaluation.Satisfied.Should().BeTrue();
			evaluation.MethodClass.Should().Be(StepUpMethodClasses.Mfa);
			evaluation.ExpiresOnUtc.Should().BeCloseTo(verified.AddMinutes(StepUpPolicyService.SigningWindowMinutes), TimeSpan.FromSeconds(1));
		}

		[TestCase("stale")]
		[TestCase("future")]
		[TestCase("generation")]
		[TestCase("department")]
		[TestCase("policy")]
		[TestCase("recovery")]
		public async Task Second_factor_evidence_that_does_not_count_leaves_the_area_unsatisfied(string why)
		{
			var verified = why switch
			{
				"stale" => _clock.Now.AddMinutes(-(StepUpPolicyService.SigningWindowMinutes + 1)),
				"future" => _clock.Now.AddMinutes(5),
				_ => _clock.Now.AddMinutes(-1)
			};
			Evidence(why == "recovery" ? MfaEvidenceKind.Recovery : MfaEvidenceKind.SecondFactor, why == "recovery" ? MfaEvidenceMethod.RecoveryCode : MfaEvidenceMethod.Totp,
				MfaEvidencePurpose.StepUp, verified, departmentId: why == "department" ? 77 : DepartmentId, generation: why == "generation" ? 3 : 4);
			if (why == "policy")
				_mfaPolicy.Setup(p => p.IsEvidenceAcceptedAsync(It.IsAny<int?>(), It.IsAny<MfaMethodScope>(), It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(false);

			(await Service().EvaluateAsync(Subject(), StepUpArea.Signatures)).Satisfied.Should().BeFalse();
		}

		[Test]
		public async Task The_area_asks_the_department_switches_of_its_own_mfa_scope()
		{
			Evidence(MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Passkey, MfaEvidencePurpose.StepUp, _clock.Now.AddMinutes(-1));
			var service = Service();
			await service.EvaluateAsync(Subject(), StepUpArea.Signatures);
			_mfaPolicy.Verify(p => p.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Login, It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_password_reentry_counts_only_where_the_department_allows_the_password()
		{
			Evidence(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, MfaEvidencePurpose.Reauthentication, _clock.Now.AddMinutes(-2));
			(await Service().EvaluateAsync(Subject(), StepUpArea.Signatures)).Satisfied.Should().BeFalse("a second factor only, by default");

			Store(new StepUpAreaSetting { Area = (int)StepUpArea.Signatures, AlternativeMethods = (int)StepUpMethodClasses.Sso });
			(await Service().EvaluateAsync(Subject(), StepUpArea.Signatures)).Satisfied.Should().BeFalse("SSO allowed is not the password allowed");

			Store(new StepUpAreaSetting { Area = (int)StepUpArea.Signatures, AlternativeMethods = (int)StepUpMethodClasses.Password });
			var evaluation = await Service().EvaluateAsync(Subject(), StepUpArea.Signatures);
			evaluation.Satisfied.Should().BeTrue();
			evaluation.MethodClass.Should().Be(StepUpMethodClasses.Password);
			evaluation.Method.Should().Be(MfaEvidenceMethod.Password);
		}

		[Test]
		public async Task A_sign_in_is_not_a_reauthentication_and_reauthentication_from_another_department_never_counts()
		{
			Store(new StepUpAreaSetting { Area = (int)StepUpArea.Approvals, AlternativeMethods = (int)(StepUpMethodClasses.Password | StepUpMethodClasses.Sso) });
			Evidence(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, MfaEvidencePurpose.Login, _clock.Now.AddMinutes(-1));
			(await Service().EvaluateAsync(Subject(), StepUpArea.Approvals)).Satisfied.Should().BeFalse("signing in is not entering the password again");

			Evidence(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Sso, MfaEvidencePurpose.Reauthentication, _clock.Now.AddMinutes(-1), departmentId: 77);
			(await Service().EvaluateAsync(Subject(), StepUpArea.Approvals)).Satisfied.Should().BeFalse("a step-up made in another department");

			Evidence(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Sso, MfaEvidencePurpose.Reauthentication, _clock.Now.AddSeconds(-5));
			(await Service().EvaluateAsync(Subject(), StepUpArea.Approvals)).MethodClass.Should().Be(StepUpMethodClasses.Sso);
		}

		[Test]
		public async Task Evidence_of_another_session_or_user_never_counts()
		{
			Store(new StepUpAreaSetting { Area = (int)StepUpArea.Signatures, AlternativeMethods = (int)StepUpMethodClasses.Password });
			var row = Evidence(MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, _clock.Now.AddMinutes(-1));
			row.SessionKey = "sid:another-device";
			var other = Evidence(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, MfaEvidencePurpose.Reauthentication, _clock.Now.AddMinutes(-1));
			other.UserId = "user-2";

			(await Service().EvaluateAsync(Subject(), StepUpArea.Signatures)).Satisfied.Should().BeFalse();
		}

		[Test]
		public void Counts_is_bound_to_user_generation_department_and_freshness()
		{
			var now = DateTime.UtcNow;
			var evidence = new MfaEvidence { UserId = UserId, AuthenticationGeneration = 4, DepartmentId = null, VerifiedOnUtc = now.AddMinutes(-1) };
			var window = TimeSpan.FromMinutes(15);
			StepUpPolicyService.Counts(evidence, Subject(), window, now).Should().BeTrue("sign-in evidence names no department");
			StepUpPolicyService.Counts(evidence, Subject(generation: 5), window, now).Should().BeFalse();
			StepUpPolicyService.Counts(new MfaEvidence { UserId = "USER-1", AuthenticationGeneration = 4, VerifiedOnUtc = now }, Subject(), window, now).Should().BeTrue();
			StepUpPolicyService.Counts(new MfaEvidence { UserId = UserId, AuthenticationGeneration = 4, VerifiedOnUtc = now.AddSeconds(20) }, Subject(), window, now)
				.Should().BeTrue("a few seconds ahead is another node's clock");
			StepUpPolicyService.Counts(null, Subject(), window, now).Should().BeFalse();
		}
	}
}
