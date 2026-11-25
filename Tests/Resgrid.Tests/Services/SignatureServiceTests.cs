using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The shared signing layer (S0): the level is decided on the server from the proof alone (a step-up inside the window, or a
	/// fresh passkey assertion bound to exactly the artifact hash); a use's floor can be raised by a department but never lowered;
	/// a refused signature writes nothing; verification recomputes the hash from the subject as stored now, never from a stored
	/// hash column; voiding is stamped once and audited; certification artifacts hash only what an officer verifies.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SignatureServiceTests
	{
		private const int DepartmentId = 10;
		private const int OtherDepartmentId = 99;
		private const string SignerId = "signer-1";
		private const string Html = "<h2>Scene safety</h2><p>Stay upwind of the spill.</p>";

		private static readonly string Hash = ProtocolContentHash.Compute(Html);

		private Mock<ISignatureRecordsRepository> _records;
		private Mock<IProtocolVersionsRepository> _versions;
		private Mock<IPersonnelCertificationRepository> _certifications;
		private Mock<IUserProfileService> _profiles;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IEventAggregator> _events;
		private SignatureService _service;
		private Dictionary<int, SignatureRecord> _stored;
		private int _originalWindow;

		[SetUp]
		public void SetUp()
		{
			// The step-up window is a static config field; pin it so the freshness cases mean what they say.
			_originalWindow = Resgrid.Config.TwoFactorConfig.StepUpVerificationWindowMinutes;
			Resgrid.Config.TwoFactorConfig.StepUpVerificationWindowMinutes = 15;

			_stored = new Dictionary<int, SignatureRecord>();
			_records = new Mock<ISignatureRecordsRepository>();
			_versions = new Mock<IProtocolVersionsRepository>();
			_certifications = new Mock<IPersonnelCertificationRepository>();
			_profiles = new Mock<IUserProfileService>();
			_roles = new Mock<IPersonnelRolesService>();
			_events = new Mock<IEventAggregator>();

			_records.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) =>
				{
					r.SignatureRecordId = 500 + _stored.Count;
					_stored[r.SignatureRecordId] = r;
					return r;
				});
			_records.Setup(x => x.SaveOrUpdateAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) =>
				{
					_stored[r.SignatureRecordId] = r;
					return r;
				});
			_records.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int departmentId, int id) => _stored.TryGetValue(id, out var r) ? r : null);

			_profiles.Setup(x => x.GetProfileByUserIdAsync(SignerId, It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = SignerId, FirstName = "Pat", LastName = "Lee" });
			_roles.Setup(x => x.GetRolesForUserAsync(SignerId, DepartmentId)).ReturnsAsync(new List<PersonnelRole>
			{
				new PersonnelRole { PersonnelRoleId = 2, DepartmentId = DepartmentId, Name = "Training Officer" },
				new PersonnelRole { PersonnelRoleId = 1, DepartmentId = DepartmentId, Name = "Captain" },
				// A role row of another department never lands in the snapshot.
				new PersonnelRole { PersonnelRoleId = 9, DepartmentId = OtherDepartmentId, Name = "Chief" }
			});

			_service = new SignatureService(_records.Object, _versions.Object, _certifications.Object, _profiles.Object, _roles.Object, _events.Object,
				Mock.Of<IProtocolVersionFilesRepository>());
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.TwoFactorConfig.StepUpVerificationWindowMinutes = _originalWindow;
		}

		private static SignatureRequest Request(SignatureUse use, string hash = null, string subjectId = "77") => new SignatureRequest
		{
			DepartmentId = DepartmentId,
			Use = use,
			SubjectId = subjectId,
			RevisionId = "round-1/step-1",
			ArtifactSha256 = hash ?? Hash,
			StatementText = "  I approve this revision for publication.  "
		};

		private static SignatureProof StepUp(DateTime verifiedOnUtc) => new SignatureProof
		{
			UserId = SignerId,
			StepUpVerifiedOnUtc = verifiedOnUtc,
			StepUpMethod = 1,
			IpAddress = "203.0.113.7",
			UserAgent = "UnitTest/1.0"
		};

		private static SignatureProof Passkey(string boundHash, DateTime verifiedOnUtc) => new SignatureProof
		{
			UserId = SignerId,
			PasskeyId = "pk-1",
			PasskeyChallengeId = "ch-1",
			PasskeyBoundSha256 = boundHash,
			PasskeyVerifiedOnUtc = verifiedOnUtc
		};

		#region Assess

		[Test]
		public void Assess_with_no_proof_or_a_session_only_proof_is_L1_session()
		{
			var now = DateTime.UtcNow;

			var none = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), null, now);
			var session = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), SignatureProof.SessionOnly(SignerId), now);

			none.Level.Should().Be(SignatureLevels.Attest);
			none.Method.Should().Be(SignatureMethods.Session);
			none.Refusal.Should().BeNull();
			session.Level.Should().Be(SignatureLevels.Attest);
			session.Method.Should().Be(SignatureMethods.Session);
			session.Evidence.Should().BeNull();
			session.Refusal.Should().BeNull();
		}

		[Test]
		public void Assess_with_a_fresh_step_up_is_L2_step_up_with_the_evidence_recorded()
		{
			var now = DateTime.UtcNow;

			var assessed = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), StepUp(now.AddMinutes(-2)), now);

			assessed.Level.Should().Be(SignatureLevels.StepUp);
			assessed.Method.Should().Be(SignatureMethods.StepUp);
			assessed.Evidence.Should().StartWith("mfa:1:");
			assessed.Refusal.Should().BeNull();
		}

		[Test]
		public void Assess_with_a_step_up_older_than_the_window_is_only_L1()
		{
			var now = DateTime.UtcNow;

			var assessed = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), StepUp(now.AddMinutes(-16)), now);

			assessed.Level.Should().Be(SignatureLevels.Attest);
			assessed.Method.Should().Be(SignatureMethods.Session);
			assessed.Refusal.Should().BeNull();
		}

		[Test]
		public void Assess_with_a_fresh_passkey_bound_to_the_same_hash_is_L2_passkey()
		{
			var now = DateTime.UtcNow;

			// The hex case of the bound hash does not matter; the bytes do.
			var assessed = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), Passkey(Hash.ToUpperInvariant(), now.AddMinutes(-1)), now);

			assessed.Level.Should().Be(SignatureLevels.StepUp);
			assessed.Method.Should().Be(SignatureMethods.Passkey);
			assessed.Evidence.Should().Be("passkey-challenge:ch-1");
			assessed.Refusal.Should().BeNull();
		}

		[Test]
		public void Assess_with_a_passkey_bound_to_a_different_hash_is_refused_as_an_artifact_mismatch()
		{
			var now = DateTime.UtcNow;
			var proof = Passkey(ProtocolContentHash.Compute("<p>Other text</p>"), now.AddMinutes(-1));
			// A fresh step-up alongside does not rescue an assertion that signed other content.
			proof.StepUpVerifiedOnUtc = now.AddMinutes(-1);

			var assessed = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), proof, now);

			assessed.Refusal.Should().Be(SignatureOutcomes.ArtifactMismatch);
			assessed.Level.Should().Be(SignatureLevels.Attest);
		}

		[Test]
		public void Assess_with_a_passkey_older_than_five_minutes_is_not_L2()
		{
			var now = DateTime.UtcNow;

			var assessed = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), Passkey(Hash, now.AddMinutes(-6)), now);

			assessed.Level.Should().Be(SignatureLevels.Attest);
			assessed.Method.Should().Be(SignatureMethods.Session);
		}

		[Test]
		public void Assess_with_a_step_up_far_in_the_future_is_not_proof_but_small_clock_skew_is()
		{
			var now = DateTime.UtcNow;

			var future = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), StepUp(now.AddMinutes(10)), now);
			var skew = SignatureService.Assess(Request(SignatureUse.ProtocolApproval), StepUp(now.AddSeconds(10)), now);

			future.Level.Should().Be(SignatureLevels.Attest);
			future.Method.Should().Be(SignatureMethods.Session);
			skew.Level.Should().Be(SignatureLevels.StepUp);
		}

		#endregion Assess

		#region SignAsync

		[Test]
		public async Task SignAsync_for_an_approval_with_only_a_session_is_refused_and_writes_nothing()
		{
			var result = await _service.SignAsync(Request(SignatureUse.ProtocolApproval), SignatureProof.SessionOnly(SignerId));

			result.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
			result.Record.Should().BeNull();
			result.Succeeded.Should().BeFalse();
			_records.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_events.Verify(x => x.SendMessage(It.IsAny<AuditEvent>()), Times.Never);
		}

		[Test]
		public async Task SignAsync_for_an_approval_with_a_fresh_step_up_writes_one_L2_record_bound_to_the_hash()
		{
			// An upper-case hash from the caller is stored lower case.
			var result = await _service.SignAsync(Request(SignatureUse.ProtocolApproval, Hash.ToUpperInvariant()), StepUp(DateTime.UtcNow.AddMinutes(-1)));

			result.Outcome.Should().Be(SignatureOutcomes.Signed);
			result.Succeeded.Should().BeTrue();
			_records.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);

			var record = result.Record;
			record.DepartmentId.Should().Be(DepartmentId);
			record.SubjectKind.Should().Be((int)SignatureSubjectKinds.ProtocolVersion);
			record.SubjectId.Should().Be("77");
			record.RevisionId.Should().Be("round-1/step-1");
			record.Intent.Should().Be((int)SignatureIntents.Approve);
			record.StatementVersion.Should().Be("protocol-approval/1");
			record.StatementText.Should().Be("I approve this revision for publication.");
			record.Level.Should().Be((int)SignatureLevels.StepUp);
			record.Method.Should().Be((int)SignatureMethods.StepUp);
			record.ArtifactSha256.Should().Be(Hash);
			record.ArtifactSha256.Should().Be(record.ArtifactSha256.ToLowerInvariant());
			record.SignerUserId.Should().Be(SignerId);
			record.SignerNameSnapshot.Should().Be("Pat Lee");
			record.SignerRoleSnapshot.Should().Be("Captain, Training Officer");
			record.IpAddress.Should().Be("203.0.113.7");
			record.UserAgent.Should().Be("UnitTest/1.0");
			record.StepUpEvidenceReference.Should().StartWith("mfa:1:");
			record.PasskeyCredentialId.Should().BeNull();
			record.VoidedOn.Should().BeNull();
			record.SignedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.SignatureRecorded && e.DepartmentId == DepartmentId &&
				e.UserId == SignerId && e.After.Contains(Hash))), Times.Once);
		}

		[Test]
		public async Task SignAsync_with_a_hash_bound_passkey_records_the_passkey_id()
		{
			var result = await _service.SignAsync(Request(SignatureUse.ProtocolApproval), Passkey(Hash, DateTime.UtcNow.AddMinutes(-1)));

			result.Succeeded.Should().BeTrue();
			result.Record.Method.Should().Be((int)SignatureMethods.Passkey);
			result.Record.Level.Should().Be((int)SignatureLevels.StepUp);
			result.Record.PasskeyCredentialId.Should().Be("pk-1");
			result.Record.StepUpEvidenceReference.Should().Be("passkey-challenge:ch-1");
		}

		[Test]
		public async Task SignAsync_with_a_passkey_bound_to_other_content_is_refused_and_writes_nothing()
		{
			var result = await _service.SignAsync(Request(SignatureUse.ProtocolApproval), Passkey(ProtocolContentHash.Compute("<p>Other</p>"), DateTime.UtcNow));

			result.Outcome.Should().Be(SignatureOutcomes.ArtifactMismatch);
			_records.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[TestCase(null)]
		[TestCase("not-a-hash")]
		[TestCase("")]
		public async Task SignAsync_without_a_sha256_artifact_hash_is_an_invalid_request(string hash)
		{
			var request = Request(SignatureUse.ProtocolApproval);
			request.ArtifactSha256 = hash;

			var result = await _service.SignAsync(request, StepUp(DateTime.UtcNow));

			result.Outcome.Should().Be(SignatureOutcomes.InvalidRequest);
			_records.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SignAsync_for_read_and_sign_with_a_session_is_an_L1_attest()
		{
			var result = await _service.SignAsync(Request(SignatureUse.ReadAndSign), SignatureProof.SessionOnly(SignerId));

			result.Succeeded.Should().BeTrue();
			result.Record.Level.Should().Be((int)SignatureLevels.Attest);
			result.Record.Method.Should().Be((int)SignatureMethods.Session);
			result.Record.SubjectKind.Should().Be((int)SignatureSubjectKinds.ProtocolAcknowledgment);
		}

		#endregion SignAsync

		#region Level floor

		[TestCase(null)]
		[TestCase(2)]
		[TestCase(1)]
		[TestCase(0)]
		[TestCase(-5)]
		public void RequiredLevel_for_an_approval_never_drops_below_the_step_up_floor(int? departmentLevel)
		{
			SignaturePolicy.RequiredLevel(SignatureUse.ProtocolApproval, departmentLevel).Should().Be(SignatureLevels.StepUp);
		}

		[Test]
		public void RequiredLevel_lets_a_department_raise_read_and_sign_and_clamps_above_the_highest_level()
		{
			SignaturePolicy.RequiredLevel(SignatureUse.ReadAndSign, null).Should().Be(SignatureLevels.Attest);
			SignaturePolicy.RequiredLevel(SignatureUse.ReadAndSign, 0).Should().Be(SignatureLevels.Attest);
			SignaturePolicy.RequiredLevel(SignatureUse.ReadAndSign, 2).Should().Be(SignatureLevels.StepUp);

			// A level that does not exist (yet) clamps to the highest defined level instead of becoming an undefined enum value.
			var clamped = SignaturePolicy.RequiredLevel(SignatureUse.ReadAndSign, 99);
			clamped.Should().Be(SignatureLevels.StepUp);
			Enum.IsDefined(typeof(SignatureLevels), clamped).Should().BeTrue();
		}

		[Test]
		public void Every_use_has_its_documented_floor()
		{
			SignatureUse.All.Should().HaveCount(7);
			SignatureUse.ChecklistWitness.Floor.Should().Be(SignatureLevels.StepUp);
			SignatureUse.CourseApproval.Floor.Should().Be(SignatureLevels.StepUp);
			SignatureUse.ProtocolApproval.Floor.Should().Be(SignatureLevels.StepUp);
			SignatureUse.ProtocolReviewStamp.Floor.Should().Be(SignatureLevels.StepUp);
			SignatureUse.CertificationVerification.Floor.Should().Be(SignatureLevels.StepUp);
			SignatureUse.ReadAndSign.Floor.Should().Be(SignatureLevels.Attest);
			SignatureUse.All.Select(u => u.Key).Should().OnlyHaveUniqueItems();
		}

		[Test]
		public void Satisfies_never_accepts_a_session_as_L2_and_a_required_passkey_only_from_a_passkey()
		{
			SignaturePolicy.Satisfies(SignatureLevels.StepUp, false, SignatureLevels.Attest, SignatureMethods.Session).Should().BeFalse();
			// A level claimed as L2 but proven only by the session is not L2.
			SignaturePolicy.Satisfies(SignatureLevels.StepUp, false, SignatureLevels.StepUp, SignatureMethods.Session).Should().BeFalse();
			SignaturePolicy.Satisfies(SignatureLevels.StepUp, false, SignatureLevels.StepUp, SignatureMethods.StepUp).Should().BeTrue();
			SignaturePolicy.Satisfies(SignatureLevels.StepUp, true, SignatureLevels.StepUp, SignatureMethods.StepUp).Should().BeFalse();
			SignaturePolicy.Satisfies(SignatureLevels.StepUp, true, SignatureLevels.StepUp, SignatureMethods.Passkey).Should().BeTrue();
		}

		[Test]
		public async Task SignAsync_with_a_department_level_below_the_floor_still_needs_a_step_up()
		{
			var request = Request(SignatureUse.ProtocolApproval);
			request.DepartmentLevel = 1;

			var result = await _service.SignAsync(request, SignatureProof.SessionOnly(SignerId));

			result.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
			_records.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SignAsync_when_the_department_requires_a_passkey_refuses_a_step_up()
		{
			var request = Request(SignatureUse.ProtocolApproval);
			request.RequirePasskey = true;

			var result = await _service.SignAsync(request, StepUp(DateTime.UtcNow.AddMinutes(-1)));

			result.Outcome.Should().Be(SignatureOutcomes.PasskeyRequired);
			_records.Verify(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SignAsync_when_the_department_requires_a_passkey_accepts_a_hash_bound_passkey()
		{
			var request = Request(SignatureUse.ProtocolApproval);
			request.RequirePasskey = true;

			var result = await _service.SignAsync(request, Passkey(Hash, DateTime.UtcNow.AddMinutes(-1)));

			result.Succeeded.Should().BeTrue();
			result.Record.Method.Should().Be((int)SignatureMethods.Passkey);
		}

		#endregion Level floor

		#region Verify

		private SignatureRecord StoredProtocolSignature(int versionId = 77, string hash = null)
		{
			var record = new SignatureRecord
			{
				SignatureRecordId = 700,
				DepartmentId = DepartmentId,
				SubjectKind = (int)SignatureSubjectKinds.ProtocolVersion,
				SubjectId = versionId.ToString(),
				Level = (int)SignatureLevels.StepUp,
				Method = (int)SignatureMethods.StepUp,
				ArtifactSha256 = hash ?? Hash,
				SignerUserId = SignerId
			};
			_stored[record.SignatureRecordId] = record;
			return record;
		}

		[Test]
		public void Verify_with_the_signed_hash_is_valid_whatever_its_hex_case()
		{
			var record = new SignatureRecord { DepartmentId = DepartmentId, ArtifactSha256 = Hash };

			_service.Verify(record, Hash).State.Should().Be(SignatureVerificationStates.Valid);
			_service.Verify(record, Hash.ToUpperInvariant()).IsValid.Should().BeTrue();
			_service.Verify(record, ProtocolContentHash.Compute("<p>Other</p>")).State.Should().Be(SignatureVerificationStates.ArtifactChanged);
		}

		[Test]
		public async Task VerifyAsync_on_unchanged_content_is_valid()
		{
			StoredProtocolSignature();
			_versions.Setup(x => x.GetByIdForDepartmentAsync(DepartmentId, 77)).ReturnsAsync(new ProtocolVersion
			{
				ProtocolVersionId = 77, DepartmentId = DepartmentId, ContentHtml = Html, ContentHash = Hash
			});

			var verification = await _service.VerifyAsync(DepartmentId, 700);

			verification.State.Should().Be(SignatureVerificationStates.Valid);
			verification.CurrentSha256.Should().Be(Hash);
		}

		[Test]
		public async Task VerifyAsync_recomputes_the_hash_from_the_stored_content_so_tampered_text_is_ArtifactChanged()
		{
			StoredProtocolSignature();
			const string tampered = "<h2>Scene safety</h2><p>Stay downwind of the spill.</p>";
			// The row's text was changed behind the lifecycle while its hash column still claims the signed value.
			_versions.Setup(x => x.GetByIdForDepartmentAsync(DepartmentId, 77)).ReturnsAsync(new ProtocolVersion
			{
				ProtocolVersionId = 77, DepartmentId = DepartmentId, ContentHtml = tampered, ContentHash = Hash
			});

			var verification = await _service.VerifyAsync(DepartmentId, 700);

			verification.State.Should().Be(SignatureVerificationStates.ArtifactChanged);
			verification.IsValid.Should().BeFalse();
			verification.CurrentSha256.Should().Be(ProtocolContentHash.Compute(tampered));
		}

		[Test]
		public async Task VerifyAsync_on_a_voided_signature_is_Voided_even_when_the_content_matches()
		{
			var record = StoredProtocolSignature();
			record.VoidedOn = DateTime.UtcNow.AddDays(-1);
			_versions.Setup(x => x.GetByIdForDepartmentAsync(DepartmentId, 77)).ReturnsAsync(new ProtocolVersion
			{
				ProtocolVersionId = 77, DepartmentId = DepartmentId, ContentHtml = Html, ContentHash = Hash
			});

			var verification = await _service.VerifyAsync(DepartmentId, 700);

			verification.State.Should().Be(SignatureVerificationStates.Voided);
		}

		[Test]
		public async Task VerifyAsync_when_the_subject_is_gone_is_SubjectMissing()
		{
			StoredProtocolSignature();
			_versions.Setup(x => x.GetByIdForDepartmentAsync(DepartmentId, 77)).ReturnsAsync((ProtocolVersion)null);

			var verification = await _service.VerifyAsync(DepartmentId, 700);

			verification.State.Should().Be(SignatureVerificationStates.SubjectMissing);
			_service.Verify(new SignatureRecord { ArtifactSha256 = Hash }, null).State.Should().Be(SignatureVerificationStates.SubjectMissing);
		}

		[Test]
		public async Task VerifyAsync_never_returns_another_departments_signature()
		{
			var record = StoredProtocolSignature();
			record.DepartmentId = OtherDepartmentId;

			(await _service.VerifyAsync(DepartmentId, 700)).Should().BeNull();
		}

		#endregion Verify

		#region Void

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		public async Task VoidAsync_without_a_reason_does_nothing(string reason)
		{
			StoredProtocolSignature();

			var voided = await _service.VoidAsync(DepartmentId, 700, "admin-1", reason);

			voided.Should().BeNull();
			_stored[700].VoidedOn.Should().BeNull();
			_records.Verify(x => x.SaveOrUpdateAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_events.Verify(x => x.SendMessage(It.IsAny<AuditEvent>()), Times.Never);
		}

		[Test]
		public async Task VoidAsync_stamps_who_when_and_why_once_and_audits_it()
		{
			StoredProtocolSignature();

			var voided = await _service.VoidAsync(DepartmentId, 700, "admin-1", "  Signed the wrong revision.  ");

			voided.VoidedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
			voided.VoidedByUserId.Should().Be("admin-1");
			voided.VoidReason.Should().Be("Signed the wrong revision.");
			voided.IsVoided.Should().BeTrue();
			_records.Verify(x => x.SaveOrUpdateAsync(It.Is<SignatureRecord>(r => r.SignatureRecordId == 700 && r.VoidedOn.HasValue), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.SignatureVoided && e.UserId == "admin-1" && e.DepartmentId == DepartmentId)), Times.Once);
		}

		[Test]
		public async Task VoidAsync_twice_keeps_the_first_stamp()
		{
			StoredProtocolSignature();
			var first = await _service.VoidAsync(DepartmentId, 700, "admin-1", "First reason");
			var firstOn = first.VoidedOn;

			var second = await _service.VoidAsync(DepartmentId, 700, "admin-2", "Second reason");

			second.VoidedOn.Should().Be(firstOn);
			second.VoidedByUserId.Should().Be("admin-1");
			second.VoidReason.Should().Be("First reason");
			_records.Verify(x => x.SaveOrUpdateAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.SignatureVoided)), Times.Once);
		}

		[Test]
		public async Task VoidAsync_of_another_departments_signature_does_nothing()
		{
			var record = StoredProtocolSignature();
			record.DepartmentId = OtherDepartmentId;

			(await _service.VoidAsync(DepartmentId, 700, "admin-1", "Not ours")).Should().BeNull();
			record.VoidedOn.Should().BeNull();
		}

		#endregion Void

		#region Certification verification

		private static PersonnelCertification Certification(DateTime? expiresOn = null) => new PersonnelCertification
		{
			PersonnelCertificationId = 55,
			DepartmentId = DepartmentId,
			UserId = "MEMBER-1",
			DepartmentCertificationTypeId = 4,
			RecievedOn = new DateTime(2025, 3, 1),
			ExpiresOn = expiresOn ?? new DateTime(2027, 3, 1),
			Number = "EMT-12345",
			Area = "State"
		};

		[Test]
		public void CertificationArtifactSha256_is_stable_and_moves_only_with_what_an_officer_verifies()
		{
			var first = _service.CertificationArtifactSha256(Certification());
			var again = _service.CertificationArtifactSha256(Certification());

			first.Should().Be(again);
			SignaturePolicy.IsSha256(first).Should().BeTrue();
			first.Should().Be(first.ToLowerInvariant());

			_service.CertificationArtifactSha256(Certification(new DateTime(2028, 3, 1))).Should().NotBe(first);

			// The holder id's case and protected text (number, issuer) are not part of the artifact.
			var recased = Certification();
			recased.UserId = "member-1";
			recased.Number = "EMT-99999";
			recased.Area = "Other";
			_service.CertificationArtifactSha256(recased).Should().Be(first);

			_service.CertificationArtifactSha256(null).Should().BeNull();
		}

		[Test]
		public async Task SignAsync_for_a_certification_verification_with_step_up_writes_an_L2_verification()
		{
			var hash = _service.CertificationArtifactSha256(Certification());

			var result = await _service.SignAsync(Request(SignatureUse.CertificationVerification, hash, "55"), StepUp(DateTime.UtcNow.AddMinutes(-1)));

			result.Succeeded.Should().BeTrue();
			result.Record.Level.Should().Be((int)SignatureLevels.StepUp);
			result.Record.SubjectKind.Should().Be((int)SignatureSubjectKinds.CertificationVerification);
			result.Record.Intent.Should().Be((int)SignatureIntents.Verify);
			result.Record.ArtifactSha256.Should().Be(hash);
		}

		[Test]
		public async Task SignAsync_for_a_certification_verification_with_only_a_session_is_refused()
		{
			var hash = _service.CertificationArtifactSha256(Certification());

			var result = await _service.SignAsync(Request(SignatureUse.CertificationVerification, hash, "55"), SignatureProof.SessionOnly(SignerId));

			result.Outcome.Should().Be(SignatureOutcomes.StepUpRequired);
		}

		[Test]
		public async Task VerifyAsync_for_a_certification_turns_ArtifactChanged_when_its_expiry_moves()
		{
			var hash = _service.CertificationArtifactSha256(Certification());
			_stored[701] = new SignatureRecord
			{
				SignatureRecordId = 701, DepartmentId = DepartmentId, SubjectKind = (int)SignatureSubjectKinds.CertificationVerification,
				SubjectId = "55", ArtifactSha256 = hash, Level = 2, Method = 1
			};
			var current = Certification();
			_certifications.Setup(x => x.GetByIdAsync(It.IsAny<object>())).ReturnsAsync(() => current);

			(await _service.VerifyAsync(DepartmentId, 701)).State.Should().Be(SignatureVerificationStates.Valid);

			current = Certification(new DateTime(2030, 1, 1));
			(await _service.VerifyAsync(DepartmentId, 701)).State.Should().Be(SignatureVerificationStates.ArtifactChanged);

			// A record that moved to another department is not this signature's subject any more.
			current = Certification();
			current.DepartmentId = OtherDepartmentId;
			(await _service.VerifyAsync(DepartmentId, 701)).State.Should().Be(SignatureVerificationStates.SubjectMissing);
		}

		#endregion Certification verification

		#region Passkey challenge and hashing

		[Test]
		public void PasskeyChallengeFor_embeds_the_hash_after_the_random_bytes_and_ChallengeEmbeds_finds_it()
		{
			var random = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

			var challenge = SignaturePolicy.PasskeyChallengeFor(Hash, random);

			challenge.Should().HaveCount(48);
			challenge.Take(16).Should().Equal(random);
			challenge.Skip(16).Should().Equal(Convert.FromHexString(Hash));
			SignaturePolicy.ChallengeEmbeds(challenge, Hash).Should().BeTrue();
			SignaturePolicy.ChallengeEmbeds(challenge, Hash.ToUpperInvariant()).Should().BeTrue();
		}

		[Test]
		public void ChallengeEmbeds_is_false_for_a_different_hash_or_a_tampered_challenge()
		{
			var challenge = SignaturePolicy.PasskeyChallengeFor(Hash, new byte[16]);

			SignaturePolicy.ChallengeEmbeds(challenge, ProtocolContentHash.Compute("<p>Other</p>")).Should().BeFalse();
			SignaturePolicy.ChallengeEmbeds(challenge.Take(47).ToArray(), Hash).Should().BeFalse();
			SignaturePolicy.ChallengeEmbeds(null, Hash).Should().BeFalse();
			SignaturePolicy.ChallengeEmbeds(challenge, "not-a-hash").Should().BeFalse();

			var tampered = (byte[])challenge.Clone();
			tampered[47] ^= 0x01;
			SignaturePolicy.ChallengeEmbeds(tampered, Hash).Should().BeFalse();
		}

		[Test]
		public void PasskeyChallengeFor_needs_a_sha256_and_sixteen_random_bytes()
		{
			Action badHash = () => SignaturePolicy.PasskeyChallengeFor("abc", new byte[16]);
			Action shortRandom = () => SignaturePolicy.PasskeyChallengeFor(Hash, new byte[15]);

			badHash.Should().Throw<ArgumentException>();
			shortRandom.Should().Throw<ArgumentException>();
		}

		[Test]
		public void HashArtifact_matches_the_protocol_content_hash_and_ShortCode_shows_the_first_eight()
		{
			SignaturePolicy.HashArtifact(Html).Should().Be(ProtocolContentHash.Compute(Html));
			SignaturePolicy.HashArtifact(Html).Should().Be(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Html))).ToLowerInvariant());

			SignaturePolicy.ShortCode("3f2a91c0" + new string('0', 56)).Should().Be("3F2A-91C0");
			SignaturePolicy.ShortCode("abc").Should().BeEmpty();
			SignaturePolicy.ShortCode(null).Should().BeEmpty();

			SignaturePolicy.IsSha256(Hash).Should().BeTrue();
			SignaturePolicy.IsSha256(Hash.Substring(1)).Should().BeFalse();
			SignaturePolicy.IsSha256(new string('g', 64)).Should().BeFalse();
		}

		#endregion Passkey challenge and hashing
	}
}
