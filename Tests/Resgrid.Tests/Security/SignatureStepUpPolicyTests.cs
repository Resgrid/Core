using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5: the one signing layer under the department's step-up policy. An optional area no longer demands L2 (a passkey
	/// requirement still does); the password or a provider sign-in entered again signs at L2 only where the area allows that kind, and the
	/// record says so; approvals follow the approvals rule and read-and-sign the acknowledgments rule.
	/// </summary>
	[TestFixture]
	public class SignatureStepUpPolicyTests
	{
		private const int DepartmentId = 42;
		private const string SignerId = "signer-1";
		private static readonly string Hash = SignaturePolicy.HashArtifact("artifact");

		private Mock<ISignatureRecordsRepository> _records;
		private List<SignatureRecord> _saved;
		private Mock<IStepUpPolicyService> _policy;
		private List<StepUpArea> _asked;

		[SetUp]
		public void SetUp()
		{
			_saved = new List<SignatureRecord>();
			_asked = new List<StepUpArea>();
			_records = new Mock<ISignatureRecordsRepository>();
			_records.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) =>
				{
					r.SignatureRecordId = 100 + _saved.Count;
					_saved.Add(r);
					return r;
				});
			Policy(null);
		}

		private void Policy(Func<StepUpArea, StepUpRule> rule)
		{
			_policy = StepUpTestPolicies.Of(rule);
			_policy.Setup(p => p.GetRuleAsync(It.IsAny<int>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, StepUpArea area, CancellationToken _) =>
				{
					_asked.Add(area);
					return rule?.Invoke(area) ?? StepUpRule.Default(area, StepUpTestPolicies.WindowMinutes);
				});
		}

		private SignatureService Service() => new(_records.Object, Mock.Of<IProtocolVersionsRepository>(), Mock.Of<IPersonnelCertificationRepository>(),
			Mock.Of<IUserProfileService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IEventAggregator>(), Mock.Of<IProtocolVersionFilesRepository>(), _policy.Object);

		private static SignatureRequest Request(SignatureUse use, StepUpArea? area = null, bool requirePasskey = false, int? departmentLevel = null) => new()
		{
			DepartmentId = DepartmentId, Use = use, SubjectId = "7", ArtifactSha256 = Hash, StatementText = "I approve.", StepUpArea = area,
			RequirePasskey = requirePasskey, DepartmentLevel = departmentLevel
		};

		private static SignatureProof Session() => SignatureProof.SessionOnly(SignerId);

		private static SignatureProof Reauthenticated(MfaEvidenceMethod method, DateTime at) => new()
		{
			UserId = SignerId, ReauthenticatedOnUtc = at, ReauthenticationMethod = (int)method
		};

		[Test]
		public async Task Strict_policy_keeps_the_L2_floor_of_an_approval()
		{
			var result = await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Session());
			result.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
			_asked.Should().Equal(StepUpArea.Signatures);
		}

		[Test]
		public async Task An_optional_signatures_area_signs_at_L1_with_the_session_and_still_records_L2_when_the_member_stepped_up()
		{
			Policy(a => StepUpTestPolicies.Rule(a, required: false));

			var plain = await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Session());
			plain.Outcome.Should().Be(SignatureOutcomes.Signed);
			plain.Record.Level.Should().Be((int)SignatureLevels.Attest);
			plain.Record.Method.Should().Be((int)SignatureMethods.Session);

			var proved = await Service().SignAsync(Request(SignatureUse.ProtocolApproval), new SignatureProof { UserId = SignerId, StepUpVerifiedOnUtc = DateTime.UtcNow, StepUpMethod = (int)MfaEvidenceMethod.Totp });
			proved.Record.Level.Should().Be((int)SignatureLevels.StepUp, "the member proved more than the area asked for");
		}

		[Test]
		public async Task An_optional_area_never_waives_a_passkey_requirement()
		{
			Policy(a => StepUpTestPolicies.Rule(a, required: false));
			(await Service().SignAsync(Request(SignatureUse.ProtocolApproval, requirePasskey: true), Session())).Outcome.Should().Be(SignatureOutcomes.PasskeyRequired);
		}

		[Test]
		public async Task A_password_reentry_signs_at_L2_only_where_the_area_allows_the_password_and_the_record_says_reauth()
		{
			var proof = Reauthenticated(MfaEvidenceMethod.Password, DateTime.UtcNow.AddMinutes(-2));
			(await Service().SignAsync(Request(SignatureUse.CertificationVerification), proof)).Outcome.Should().Be(SignatureOutcomes.StepUpRequired);

			Policy(a => StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Sso));
			(await Service().SignAsync(Request(SignatureUse.CertificationVerification), proof)).Outcome.Should().Be(SignatureOutcomes.StepUpRequired,
				"SSO allowed does not allow the password");

			Policy(a => StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Password));
			var signed = await Service().SignAsync(Request(SignatureUse.CertificationVerification), proof);
			signed.Outcome.Should().Be(SignatureOutcomes.Signed);
			signed.Record.Level.Should().Be((int)SignatureLevels.StepUp);
			signed.Record.Method.Should().Be((int)SignatureMethods.StepUp);
			signed.Record.StepUpEvidenceReference.Should().StartWith("reauth:password:");
		}

		[Test]
		public async Task A_stale_or_future_reauthentication_is_not_proof()
		{
			Policy(a => StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Password | StepUpMethodClasses.Sso));
			(await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Reauthenticated(MfaEvidenceMethod.Sso, DateTime.UtcNow.AddMinutes(-(StepUpTestPolicies.WindowMinutes + 1)))))
				.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
			(await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Reauthenticated(MfaEvidenceMethod.Sso, DateTime.UtcNow.AddMinutes(5))))
				.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
			var sso = await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Reauthenticated(MfaEvidenceMethod.Sso, DateTime.UtcNow.AddMinutes(-1)));
			sso.Record.StepUpEvidenceReference.Should().StartWith("reauth:sso:");
		}

		[Test]
		public async Task A_recovery_code_in_the_reauthentication_slot_never_signs()
		{
			Policy(a => StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Password | StepUpMethodClasses.Sso));
			(await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Reauthenticated(MfaEvidenceMethod.RecoveryCode, DateTime.UtcNow)))
				.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
		}

		[Test]
		public async Task Approvals_follow_the_approvals_rule_and_read_and_sign_the_acknowledgments_rule()
		{
			Policy(a => a == StepUpArea.Approvals ? StepUpTestPolicies.Rule(a, required: false) : null);
			var step = SignatureUse.ForRequestStep(SignatureSubjectKinds.ProtocolVersion, SignatureIntents.Approve, SignatureLevels.StepUp, "approval/1");

			(await Service().SignAsync(Request(step, StepUpArea.Approvals, departmentLevel: (int)SignatureLevels.StepUp), Session())).Outcome.Should().Be(SignatureOutcomes.Signed);
			(await Service().SignAsync(Request(step, departmentLevel: (int)SignatureLevels.StepUp), Session())).Outcome.Should().Be(SignatureOutcomes.StepUpRequired,
				"a signature request step without an area is a signature");

			_asked.Clear();
			await Service().SignAsync(Request(SignatureUse.ReadAndSign, departmentLevel: (int)SignatureLevels.StepUp), Session());
			_asked.Should().Equal(StepUpArea.Acknowledgments);
		}

		[Test]
		public async Task A_policy_that_cannot_be_read_signs_under_the_strict_default()
		{
			_policy.Setup(p => p.GetRuleAsync(It.IsAny<int>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("down"));
			(await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Session())).Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
			(await Service().SignAsync(Request(SignatureUse.ProtocolApproval), Reauthenticated(MfaEvidenceMethod.Password, DateTime.UtcNow))).Outcome
				.Should().Be(SignatureOutcomes.StepUpRequired, "the strict default accepts a second factor only");
		}

		[Test]
		public void Assess_without_a_rule_never_accepts_a_reauthentication()
		{
			SignatureService.Assess(Request(SignatureUse.ProtocolApproval), Reauthenticated(MfaEvidenceMethod.Password, DateTime.UtcNow), DateTime.UtcNow).Level
				.Should().Be(SignatureLevels.Attest);
		}
	}
}
