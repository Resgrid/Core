using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Signatures;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Signatures
{
	/// <summary>
	/// e-Signature S3: multi-signer ordered requests and the evaluator platform on S2's requests. Ordering (a sequential step waits;
	/// a parallel group completes only when all of it signed); roles and levels (member L1/L2 through the real signing layer, never
	/// below the role's floor; external L4); decline, void and expiry with member notifications; completion seals through S1 and
	/// fails closed; evaluator eligibility; the Readiness gate (remote L4 refused without Readiness Pro, in person allowed);
	/// reminders claimed once; Records signatures untouched; and workflow payloads without contact details.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class MultiSignerRequestTests
	{
		private const int Dept = 21;
		private const string Requester = "11111111-0000-4000-8000-000000000001";
		private const string Officer = "22222222-0000-4000-8000-000000000002";
		private const string Chief = "33333333-0000-4000-8000-000000000003";
		private const string Medic = "44444444-0000-4000-8000-000000000004";
		private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

		private string _baseUrl;
		private List<SignatureRequestRecord> _requests;
		private List<SignatureRequestSigner> _signers;
		private List<SignatureRecord> _records;
		private List<SignatureCapture> _captures;
		private List<SignatureRequestEvent> _events;
		private List<SignatureStepWorkflowEvent> _stepEvents;
		private List<(List<string> Users, bool Reminder)> _stepNotices;
		private List<(List<string> Users, int Status)> _closedNotices;
		private List<MailMessage> _mail;
		private FakeSource _source;
		private Mock<IDocumentSealingService> _sealing;
		private Mock<IReadinessAccessService> _readiness;
		private Mock<ILmsAccessService> _lms;
		private Mock<ISignerEligibilityService> _eligibility;
		private Mock<IESignProvider> _provider;
		private Mock<ISignatureSignersClaims> _claims;
		private HashSet<string> _eligible;
		private SignatureRequestService _service;

		/// <summary>Records the reminder claims the repository mock sees (the conditional update's semantics; real SQL in MultiSignerSignatureDatabaseTests).</summary>
		public interface ISignatureSignersClaims { bool Claim(int signerId, int count); }

		private sealed class FakeSource : ISignatureSubjectSource
		{
			public SignatureSubjectKinds Kind { get; set; } = SignatureSubjectKinds.SkillEvaluation;
			public string Revision { get; set; } = new string('e', 64);
			public int SignedCalls { get; private set; }

			public Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId) =>
				Task.FromResult(new SignatureSubjectInfo { Kind = Kind, SubjectId = subjectId, Title = "Ladder raise evaluation", RevisionId = "1", RevisionSha256 = Revision, DocumentHtml = "<html><body>JPR</body></html>" });

			public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default)
			{
				SignedCalls++;
				return Task.CompletedTask;
			}
		}

		[SetUp]
		public void SetUp()
		{
			_baseUrl = SystemBehaviorConfig.ResgridBaseUrl;
			SystemBehaviorConfig.ResgridBaseUrl = "https://resgrid.test";
			_requests = new List<SignatureRequestRecord>();
			_signers = new List<SignatureRequestSigner>();
			_records = new List<SignatureRecord>();
			_captures = new List<SignatureCapture>();
			_events = new List<SignatureRequestEvent>();
			_stepEvents = new List<SignatureStepWorkflowEvent>();
			_stepNotices = new List<(List<string>, bool)>();
			_closedNotices = new List<(List<string>, int)>();
			_mail = new List<MailMessage>();
			_eligible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_source = new FakeSource();

			var requests = new Mock<ISignatureRequestsRepository>();
			requests.Setup(x => x.InsertAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRequestRecord r, CancellationToken _, bool __) => { r.SignatureRequestId = _requests.Count + 1; _requests.Add(r); return r; });
			requests.Setup(x => x.UpdateAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SignatureRequestRecord r, CancellationToken _, bool __) => r);
			requests.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _requests.FirstOrDefault(r => r.DepartmentId == d && r.SignatureRequestId == id));
			requests.Setup(x => x.GetByIdsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => _requests.Where(r => r.DepartmentId == d && ids.Contains(r.SignatureRequestId)).ToList());
			requests.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => _requests.Where(r => r.DepartmentId == d && r.SubjectKind == k && r.SubjectId == s).ToList());
			requests.Setup(x => x.GetDueExpiryAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync((DateTime now, int take) => _requests.Where(r => r.IsOpen && r.ExpiresOn <= now).ToList());

			_claims = new Mock<ISignatureSignersClaims>();
			_claims.Setup(x => x.Claim(It.IsAny<int>(), It.IsAny<int>())).Returns(true);
			var signers = new Mock<ISignatureRequestSignersRepository>();
			signers.Setup(x => x.InsertAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRequestSigner s, CancellationToken _, bool __) => { s.SignatureRequestSignerId = _signers.Count + 100; _signers.Add(s); return s; });
			signers.Setup(x => x.UpdateAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SignatureRequestSigner s, CancellationToken _, bool __) => s);
			signers.Setup(x => x.GetForRequestAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _signers.Where(s => s.DepartmentId == d && s.SignatureRequestId == id).ToList());
			signers.Setup(x => x.GetForRequestsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => _signers.Where(s => s.DepartmentId == d && ids.Contains(s.SignatureRequestId)).ToList());
			signers.Setup(x => x.GetPendingMemberStepsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((int d, string u, DateTime now, int take) => _signers.Where(s => s.DepartmentId == d && s.IsMember && string.Equals(s.UserId, u, StringComparison.OrdinalIgnoreCase)
					&& s.Status == (int)SignatureSignerStatuses.Pending && _requests.Any(r => r.SignatureRequestId == s.SignatureRequestId && r.IsOpen && r.ExpiresOn > now)).ToList());
			signers.Setup(x => x.GetDueRemindersAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime now, int take) => _signers.Where(s => s.NextReminderOn.HasValue && s.NextReminderOn <= now && !s.IsDone
					&& _requests.Any(r => r.SignatureRequestId == s.SignatureRequestId && r.IsOpen && r.ExpiresOn > now)).ToList());
			signers.Setup(x => x.ClaimReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int count, DateTime? next, DateTime now, CancellationToken _) =>
				{
					var s = _signers.First(x => x.SignatureRequestSignerId == id);
					if (s.ReminderCount != count || !s.NextReminderOn.HasValue || !_claims.Object.Claim(id, count)) return false;
					// The row moves on in the store; the caller's copy is a separate object in real life (a fresh read).
					return true;
				});

			var records = new Mock<ISignatureRecordsRepository>();
			records.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => { r.SignatureRecordId = _records.Count + 500; _records.Add(r); return r; });
			records.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => _records.Where(r => r.DepartmentId == d && r.SubjectKind == k && r.SubjectId == s).ToList());

			var captures = new Mock<ISignatureCapturesRepository>();
			captures.Setup(x => x.InsertAsync(It.IsAny<SignatureCapture>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureCapture c, CancellationToken _, bool __) => { _captures.Add(c); return c; });
			captures.Setup(x => x.GetForRequestAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _captures.Where(c => c.SignatureRequestId == id).ToList());

			_sealing = new Mock<IDocumentSealingService>();
			// Sealing is not configured: the seal fails closed ("sealing pending"), it never comes back sealed.
			_sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SealHtmlRequest r, CancellationToken _) => new SealOutcome { Document = new SealedDocument { SealedDocumentId = 88, Status = (int)SealedDocumentStatuses.Pending }, Bytes = new byte[] { 1 }, Reason = "sealing_sidecar_not_configured" });

			_provider = new Mock<IESignProvider>();
			_provider.SetupGet(x => x.IsAvailable).Returns(true);
			_provider.Setup(x => x.CreateEnvelopeAsync(It.IsAny<string>(), It.IsAny<ESignEnvelopeRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string t, ESignEnvelopeRequest e, CancellationToken _) => new ESignEnvelope
				{
					EnvelopeId = "env_s3",
					Recipients = e.Recipients.Select(r => new ESignRecipient { Reference = r.Reference, ProviderRecipientId = "r" + r.Reference, SigningOrder = r.SigningOrder, SigningToken = "tok" + r.Reference }).ToList()
				});
			var accounts = new Mock<IESignAccountService>();
			accounts.Setup(x => x.GetAsync(Dept)).ReturnsAsync(new ESignProviderAccount { DepartmentId = Dept, IsActive = true, ApiTokenEncrypted = "enc" });
			accounts.Setup(x => x.GetApiTokenAsync(Dept)).ReturnsAsync("api_token");

			_readiness = new Mock<IReadinessAccessService>();
			_lms = new Mock<ILmsAccessService>();

			var pdf = new Mock<IPdfProvider>();
			pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Returns(Encoding.ASCII.GetBytes("%PDF-1.4 /Type /Page"));
			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => "enc:" + s);
			encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s.StartsWith("enc:") ? s.Substring(4) : throw new InvalidOperationException());
			var email = new Mock<IEmailSender>();
			email.Setup(x => x.SendEmail(It.IsAny<MailMessage>())).Callback((MailMessage m) => _mail.Add(m)).ReturnsAsync(true);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Mesa Flats ERT" });
			departments.Setup(x => x.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(new[] { Requester, Officer, Chief, Medic }, StringComparer.OrdinalIgnoreCase));

			var events = new Mock<IEventAggregator>();
			events.Setup(x => x.SendMessage(It.IsAny<SignatureRequestEvent>())).Callback((SignatureRequestEvent e) => _events.Add(e));
			events.Setup(x => x.SendMessage(It.IsAny<SignatureStepWorkflowEvent>())).Callback((SignatureStepWorkflowEvent e) => _stepEvents.Add(e));

			_eligibility = new Mock<ISignerEligibilityService>();
			_eligibility.Setup(x => x.IsEligibleAsync(Dept, It.IsAny<string>(), It.IsAny<SignerEligibility>())).ReturnsAsync((int d, string u, SignerEligibility e) => _eligible.Contains(u));

			var notifications = new Mock<ISignatureNotificationService>();
			notifications.Setup(x => x.NotifyStepReadyAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.Callback((SignatureRequestRecord r, IReadOnlyCollection<string> users, bool reminder, CancellationToken _) => _stepNotices.Add((users.ToList(), reminder))).ReturnsAsync(1);
			notifications.Setup(x => x.NotifyClosedAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
				.Callback((SignatureRequestRecord r, IReadOnlyCollection<string> users, CancellationToken _) => _closedNotices.Add((users.ToList(), r.Status))).ReturnsAsync(1);

			// The real signing layer decides member levels (S0): step-up or a hash-bound passkey for L2, the session for L1.
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool _) => new UserProfile { UserId = id, FirstName = "M", LastName = id.Substring(0, 4) });
			var memberSignatures = new SignatureService(records.Object, Mock.Of<IProtocolVersionsRepository>(), Mock.Of<IPersonnelCertificationRepository>(), profiles.Object,
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IEventAggregator>(), Mock.Of<IProtocolVersionFilesRepository>(), Resgrid.Tests.Security.StepUpTestPolicies.Strict());

			_service = new SignatureRequestService(requests.Object, signers.Object, captures.Object, records.Object, Mock.Of<IESignWebhookEventsRepository>(),
				new Mock<IRmsSignaturesRepository>(MockBehavior.Strict).Object, new[] { (ISignatureSubjectSource)_source }, _provider.Object, accounts.Object, _sealing.Object, pdf.Object,
				encryption.Object, email.Object, Mock.Of<ITextMessageProvider>(), departments.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<IBusinessOperationsAccessService>(),
				_readiness.Object, Mock.Of<IIncidentBackOfficeAccessService>(), _lms.Object, Mock.Of<ICacheProvider>(), events.Object, memberSignatures, _eligibility.Object, notifications.Object, Mock.Of<IDepartmentDataProtectionService>(), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()));
		}

		[TearDown]
		public void TearDown() => SystemBehaviorConfig.ResgridBaseUrl = _baseUrl;

		private static SignatureStepInput MemberStep(string userId, int order, SignatureSignerRoles role = SignatureSignerRoles.Member, SignatureLevels level = SignatureLevels.Attest,
			SignerEligibility eligibility = null) =>
			new SignatureStepInput { Kind = SignatureSignerKinds.Member, UserId = userId, Order = order, Role = role, Level = level, Eligibility = eligibility, StepLabel = role.ToString() };

		private static SignatureStepInput ExternalStep(string name, int order, SignatureSignerRoles role = SignatureSignerRoles.Vendor) =>
			new SignatureStepInput { Kind = SignatureSignerKinds.External, ExternalName = name, ExternalEmail = "vendor@example.com", ExternalPhone = "+1 555 010 4040", Order = order, Role = role };

		private Task<SignatureRequestResult> CreateAsync(bool ordered, SignatureRequestChannels channel, params SignatureStepInput[] steps) => _service.CreateMultiSignerAsync(new MultiSignerRequestInput
		{
			DepartmentId = Dept, SubjectKind = _source.Kind, SubjectId = "eval-1", Ordered = ordered, ExternalChannel = channel, Steps = steps.ToList(), RequestedByUserId = Requester, Culture = "en"
		});

		private static SignatureProof Session(string userId) => SignatureProof.SessionOnly(userId, "10.0.0.1", "test");

		private static SignatureProof SteppedUp(string userId)
		{
			var proof = Session(userId);
			proof.StepUpVerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1);
			proof.StepUpMethod = 1;
			return proof;
		}

		private Task<SignatureRequestResult> SignAsync(int requestId, SignatureProof proof, int? signerId = null) =>
			_service.SignStepAsync(new SignatureStepSignInput { DepartmentId = Dept, SignatureRequestId = requestId, SignatureRequestSignerId = signerId, Proof = proof });

		#region Ordering

		[Test]
		public async Task A_sequential_step_waits_until_every_earlier_step_has_signed()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Chief, 2));
			var id = created.Request.SignatureRequestId;

			_stepNotices.Should().ContainSingle().Which.Users.Should().Equal(Officer);
			(await SignAsync(id, Session(Chief))).Refusal.Should().Be(SignatureRequestRefusals.NotYourTurn);
			(await _service.GetPendingForUserAsync(Dept, Chief)).Should().BeEmpty("it is not the chief's turn yet");
			(await _service.GetPendingForUserAsync(Dept, Officer)).Should().ContainSingle().Which.SignatureRequestId.Should().Be(id);

			var first = await SignAsync(id, Session(Officer));
			first.Succeeded.Should().BeTrue();
			_requests.Single().Status.Should().Be((int)SignatureRequestStatuses.PartiallySigned);
			_stepNotices.Last().Users.Should().Equal(Chief);
			(await _service.GetPendingForUserAsync(Dept, Chief)).Should().ContainSingle();

			(await SignAsync(id, Session(Chief))).Succeeded.Should().BeTrue();
			_requests.Single().Status.Should().Be((int)SignatureRequestStatuses.Completed);
		}

		[Test]
		public async Task Each_member_turn_raises_your_turn_once_and_an_outside_signer_never_does()
		{
			// Round 7C, workflow trigger 270.
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Chief, 2),
				ExternalStep("Acme Hydrant Co", 3));
			var id = created.Request.SignatureRequestId;

			_stepEvents.Should().ContainSingle().Which.UserId.Should().Be(Officer);
			(await SignAsync(id, Session(Chief))).Refusal.Should().Be(SignatureRequestRefusals.NotYourTurn);
			_stepEvents.Should().HaveCount(1, "a refused signature starts no turn");

			(await SignAsync(id, Session(Officer))).Succeeded.Should().BeTrue();
			_stepEvents.Should().HaveCount(2);
			var chief = _stepEvents.Last();
			chief.Trigger.Should().Be(WorkflowTriggerEventType.SignatureStepAssigned);
			(chief.SignatureRequestId, chief.UserId, chief.StepOrder, chief.SignerRole, chief.RequestedByUserId).Should().Be((id, Chief, 2, "Member", Requester));

			(await SignAsync(id, Session(Chief))).Succeeded.Should().BeTrue();
			_stepEvents.Should().HaveCount(2, "the outside signer's turn raises nothing");
			var json = JsonConvert.SerializeObject(_stepEvents);
			json.Should().NotContain("Acme").And.NotContain("vendor@example.com").And.NotContain("555");
		}

		[Test]
		public async Task A_parallel_group_completes_only_when_all_of_it_signed()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Medic, 1), MemberStep(Chief, 2));
			var id = created.Request.SignatureRequestId;
			_stepNotices.Single().Users.Should().BeEquivalentTo(new[] { Officer, Medic }, "both steps of the first group start together");

			(await SignAsync(id, Session(Medic))).Succeeded.Should().BeTrue();
			(await SignAsync(id, Session(Chief))).Refusal.Should().Be(SignatureRequestRefusals.NotYourTurn, "the officer in the same group has not signed");

			(await SignAsync(id, Session(Officer))).Succeeded.Should().BeTrue();
			(await SignAsync(id, Session(Chief))).Succeeded.Should().BeTrue();
			_requests.Single().Status.Should().Be((int)SignatureRequestStatuses.Completed);
		}

		[Test]
		public async Task An_unordered_request_lets_every_step_sign_at_once()
		{
			var created = await CreateAsync(false, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Chief, 2));
			_stepNotices.Single().Users.Should().BeEquivalentTo(new[] { Officer, Chief });
			(await SignAsync(created.Request.SignatureRequestId, Session(Chief))).Succeeded.Should().BeTrue();
		}

		#endregion

		#region Roles and levels

		[Test]
		public async Task An_L2_step_needs_a_fresh_step_up_and_an_L1_step_signs_in_the_session()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1, level: SignatureLevels.Attest), MemberStep(Chief, 2, level: SignatureLevels.StepUp));
			var id = created.Request.SignatureRequestId;

			var l1 = await SignAsync(id, Session(Officer));
			l1.Succeeded.Should().BeTrue();
			l1.Record.Level.Should().Be((int)SignatureLevels.Attest);
			l1.Record.SignatureRequestId.Should().Be(id, "the member's record answers the request");
			l1.Record.ArtifactSha256.Should().Be(_source.Revision);

			(await SignAsync(id, Session(Chief))).Refusal.Should().Be(SignatureRequestRefusals.StepUpRequired);
			var l2 = await SignAsync(id, SteppedUp(Chief));
			l2.Succeeded.Should().BeTrue();
			l2.Record.Level.Should().Be((int)SignatureLevels.StepUp);
			l2.Record.Method.Should().Be((int)SignatureMethods.StepUp);
		}

		[Test]
		public async Task A_role_never_signs_below_its_floor_and_roles_are_kept_to_their_kind()
		{
			// An evaluator, certifying official, witness or recipient asked for at L1 still signs at L2.
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1, SignatureSignerRoles.Evaluator, SignatureLevels.Attest));
			_signers.Single().RequiredLevel.Should().Be((int)SignatureLevels.StepUp);
			(await SignAsync(created.Request.SignatureRequestId, Session(Officer))).Refusal.Should().Be(SignatureRequestRefusals.StepUpRequired);
			SignatureOrdering.IntentFor(SignatureSignerRoles.Evaluator).Should().Be(SignatureIntents.Evaluate);
			SignatureOrdering.IntentFor(SignatureSignerRoles.Vendor).Should().Be(SignatureIntents.Accept);

			(await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1, SignatureSignerRoles.Vendor))).Refusal.Should().Be(SignatureRequestRefusals.InvalidRequest, "a vendor is never a member step");
			(await CreateAsync(true, SignatureRequestChannels.InPerson, ExternalStep("Acme", 1, SignatureSignerRoles.CertifyingOfficial))).Refusal.Should().Be(SignatureRequestRefusals.InvalidRequest, "a certifying official is a member");
			(await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Officer, 2))).Refusal.Should().Be(SignatureRequestRefusals.InvalidRequest, "one member, one step");
			(await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep("not-a-member", 1))).Refusal.Should().Be(SignatureRequestRefusals.NotASigner);
		}

		[Test]
		public async Task An_external_step_is_L4_and_signs_in_person_only_when_it_is_its_turn()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1, level: SignatureLevels.StepUp), ExternalStep("Acme Pumps", 2));
			var id = created.Request.SignatureRequestId;
			var vendor = _signers.Single(s => !s.IsMember);
			vendor.RequiredLevel.Should().Be((int)SignatureLevels.External);
			SignatureOrdering.StepLevel(vendor).Should().Be(SignatureLevels.External);

			var early = await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = id, BoundRevisionSha256 = _source.Revision, PrintedName = "Pat Vendor", SignatureImageDataUrl = Png, ConsentGiven = true, CapturedByUserId = Requester
			});
			early.Refusal.Should().Be(SignatureRequestRefusals.NotYourTurn);

			(await SignAsync(id, SteppedUp(Officer))).Succeeded.Should().BeTrue();
			var captured = await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = id, BoundRevisionSha256 = _source.Revision, PrintedName = "Pat Vendor", SignatureImageDataUrl = Png, ConsentGiven = true, CapturedByUserId = Requester
			});
			captured.Succeeded.Should().BeTrue();
			captured.Record.Level.Should().Be((int)SignatureLevels.External);
			_requests.Single().Status.Should().Be((int)SignatureRequestStatuses.Completed);
		}

		[Test]
		public async Task A_subject_that_moved_on_cannot_be_signed()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1));
			_source.Revision = new string('f', 64);
			(await SignAsync(created.Request.SignatureRequestId, Session(Officer))).Refusal.Should().Be(SignatureRequestRefusals.RevisionChanged);
			_records.Should().BeEmpty();
		}

		#endregion

		#region Decline, void, expiry and completion

		[Test]
		public async Task A_member_declining_closes_the_request_and_tells_the_requester_and_other_members()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Chief, 2));
			var declined = await _service.DeclineStepAsync(Dept, created.Request.SignatureRequestId, null, Officer, "Not my evaluation");

			declined.Succeeded.Should().BeTrue();
			var request = _requests.Single();
			request.Status.Should().Be((int)SignatureRequestStatuses.Declined);
			request.DeclineReason.Should().Be("Not my evaluation");
			_signers.First().DeclineReason.Should().Be("Not my evaluation");
			_signers.Should().OnlyContain(s => s.NextReminderOn == null, "a closed request reminds nobody");
			_closedNotices.Single().Users.Should().BeEquivalentTo(new[] { Requester, Chief }, "the member who declined is not told");
			_events.Last().Change.Should().Be(SignatureRequestChanges.Declined);
			(await SignAsync(request.SignatureRequestId, Session(Chief))).Refusal.Should().Be(SignatureRequestRefusals.RequestClosed);
			(await _service.DeclineStepAsync(Dept, request.SignatureRequestId, null, Chief, "x")).Refusal.Should().Be(SignatureRequestRefusals.RequestClosed);
		}

		[Test]
		public async Task Voiding_and_expiry_close_the_request_and_notify_members()
		{
			var voided = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1));
			(await _service.VoidAsync(Dept, voided.Request.SignatureRequestId, Requester, "wrong evaluator")).Status.Should().Be((int)SignatureRequestStatuses.Voided);
			_closedNotices.Last().Users.Should().Equal(Officer);
			_closedNotices.Last().Status.Should().Be((int)SignatureRequestStatuses.Voided);

			var expiring = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Chief, 1));
			expiring.Request.ExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			(await _service.ExpireDueAsync(DateTime.UtcNow, 10)).Should().Be(1);
			expiring.Request.Status.Should().Be((int)SignatureRequestStatuses.Expired);
			_closedNotices.Last().Users.Should().BeEquivalentTo(new[] { Requester, Chief });
			_events.Last().Change.Should().Be(SignatureRequestChanges.Expired);
			(await _service.GetPendingForUserAsync(Dept, Chief)).Should().BeEmpty();
		}

		[Test]
		public async Task Completion_seals_the_evidence_through_S1_and_fails_closed()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1, SignatureSignerRoles.Evaluator, SignatureLevels.StepUp), ExternalStep("Outside Instructor", 2, SignatureSignerRoles.Witness));
			var id = created.Request.SignatureRequestId;
			(await SignAsync(id, SteppedUp(Officer))).Succeeded.Should().BeTrue();
			_sealing.Verify(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()), Times.Never, "nothing is sealed before every step signed");

			(await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = id, BoundRevisionSha256 = _source.Revision, PrintedName = "Ola Outside", SignatureImageDataUrl = Png, ConsentGiven = true, CapturedByUserId = Requester
			})).Succeeded.Should().BeTrue();

			_sealing.Verify(x => x.SealHtmlAsync(It.Is<SealHtmlRequest>(r => r.RevisionSha256 == _source.Revision && r.SignatureRequestId == id && r.Role == SealedDocumentRoles.Sealed
				&& r.LongTermSubject && r.Signers.Count == 2 && r.Signers[0].Level == (int)SignatureLevels.StepUp && r.Signers[0].Role == "Evaluator" && r.Signers[0].Name != null
				&& r.Signers[1].Level == (int)SignatureLevels.External && r.Signers[1].Name == "Ola Outside"
				&& r.Html.Contains("Ola Outside") && r.Html.Contains("L2")), It.IsAny<CancellationToken>()), Times.Once);
			var request = _requests.Single();
			request.Status.Should().Be((int)SignatureRequestStatuses.Completed);
			request.SealedDocumentId.Should().Be(88, "the pending seal is recorded; worker 90 retries it and it is never shown as sealed meanwhile");
			_source.SignedCalls.Should().Be(2);
			_closedNotices.Last().Users.Should().BeEquivalentTo(new[] { Officer }, "the member signers learn it completed (the requester captured the last signature)");
		}

		[Test]
		public async Task A_sealing_failure_leaves_the_signatures_standing_without_a_sealed_document()
		{
			_sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("sidecar down"));
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1));
			(await SignAsync(created.Request.SignatureRequestId, Session(Officer))).Succeeded.Should().BeTrue();
			_requests.Single().Status.Should().Be((int)SignatureRequestStatuses.Completed);
			_requests.Single().SealedDocumentId.Should().BeNull();
			_records.Should().ContainSingle();
		}

		#endregion

		#region Evaluator mode

		[Test]
		public async Task Any_eligible_member_may_take_an_open_evaluator_step_and_cannot_also_certify()
		{
			var rule = new SignerEligibility { CertificationTypeIds = { 12 } };
			var created = await _service.CreateEvaluationAsync(new EvaluationRequestInput
			{
				DepartmentId = Dept, SubjectId = "eval-1", Eligibility = rule, CertifyingEligibility = rule, RequestedByUserId = Requester, Culture = "en"
			});
			created.Succeeded.Should().BeTrue();
			var id = created.Request.SignatureRequestId;
			_signers.Should().HaveCount(2);
			_signers[0].Role.Should().Be((int)SignatureSignerRoles.Evaluator);
			_signers[0].UserId.Should().BeNull();
			SignerEligibility.Parse(_signers[0].EligibilityJson).CertificationTypeIds.Should().Equal(12);
			_signers[1].Role.Should().Be((int)SignatureSignerRoles.CertifyingOfficial);

			(await SignAsync(id, SteppedUp(Medic))).Refusal.Should().Be(SignatureRequestRefusals.NotEligible);

			_eligible.Add(Officer);
			_eligible.Add(Chief);
			var evaluated = await SignAsync(id, SteppedUp(Officer));
			evaluated.Succeeded.Should().BeTrue();
			evaluated.Record.Intent.Should().Be((int)SignatureIntents.Evaluate);
			_signers[0].UserId.Should().Be(Officer, "the open step records who evaluated");
			(await SignAsync(id, SteppedUp(Officer))).Refusal.Should().Be(SignatureRequestRefusals.NotASigner, "an evaluator cannot certify their own evaluation");

			var certified = await SignAsync(id, SteppedUp(Chief));
			certified.Succeeded.Should().BeTrue();
			certified.Record.Intent.Should().Be((int)SignatureIntents.Certify);
			_requests.Single().Status.Should().Be((int)SignatureRequestStatuses.Completed);
		}

		[Test]
		public async Task A_named_evaluator_must_be_eligible_and_an_outside_evaluator_by_link_needs_the_LMS()
		{
			var rule = new SignerEligibility { PersonnelRoleIds = { 3 } };
			(await _service.CreateEvaluationAsync(new EvaluationRequestInput { DepartmentId = Dept, SubjectId = "eval-1", Eligibility = rule, EvaluatorUserId = Medic, RequestedByUserId = Requester }))
				.Refusal.Should().Be(SignatureRequestRefusals.NotEligible);

			var remote = new EvaluationRequestInput
			{
				DepartmentId = Dept, SubjectId = "eval-1", ExternalEvaluatorName = "Outside Evaluator", ExternalEvaluatorEmail = "eval@example.com",
				ExternalChannel = SignatureRequestChannels.Remote, RequestedByUserId = Requester
			};
			_lms.Setup(x => x.GetActiveAddonStateAsync(Dept)).ReturnsAsync(false);
			(await _service.CreateEvaluationAsync(remote)).Refusal.Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);

			remote.ExternalChannel = SignatureRequestChannels.InPerson;
			(await _service.CreateEvaluationAsync(remote)).Succeeded.Should().BeTrue("in-person L4 is free everywhere");

			_lms.Setup(x => x.GetActiveAddonStateAsync(Dept)).ReturnsAsync(true);
			remote.ExternalChannel = SignatureRequestChannels.Remote;
			var linked = await _service.CreateEvaluationAsync(remote);
			linked.Succeeded.Should().BeTrue();
			linked.Request.Channel.Should().Be((int)SignatureRequestChannels.Remote);
			_mail.Should().ContainSingle(m => m.To.Single().Address == "eval@example.com", "the outside evaluator's link goes out when it is their turn");
		}

		[Test]
		public async Task The_eligibility_resolver_takes_a_listed_role_or_a_valid_certification_of_active_members_only()
		{
			var certifications = new Mock<ICertificationService>();
			var roles = new Mock<IPersonnelRolesService>();
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(new[] { Officer, Chief }, StringComparer.OrdinalIgnoreCase));
			roles.Setup(x => x.GetRolesForUserAsync(Officer, Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 3, DepartmentId = Dept } });
			roles.Setup(x => x.GetRolesForUserAsync(It.Is<string>(u => u != Officer), Dept)).ReturnsAsync(new List<PersonnelRole>());
			certifications.Setup(x => x.GetCertificationSettingsAsync(Dept)).ReturnsAsync(new DepartmentCertificationSettings());
			certifications.Setup(x => x.GetCertificationTypeByIdAsync(12)).ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 12, DepartmentId = Dept, IsActive = true });
			certifications.Setup(x => x.GetCertificationsByUserIdAsync(Chief)).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { PersonnelCertificationId = 1, DepartmentId = Dept, UserId = Chief, DepartmentCertificationTypeId = 12, ExpiresOn = DateTime.UtcNow.AddDays(-3) }
			});
			certifications.Setup(x => x.GetCertificationsByUserIdAsync(Medic)).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { PersonnelCertificationId = 2, DepartmentId = Dept, UserId = Medic, DepartmentCertificationTypeId = 12, ExpiresOn = DateTime.UtcNow.AddYears(1) }
			});
			var resolver = new SignerEligibilityService(certifications.Object, roles.Object, departments.Object);

			(await resolver.IsEligibleAsync(Dept, Officer.ToLowerInvariant(), new SignerEligibility { PersonnelRoleIds = { 3 } })).Should().BeTrue();
			(await resolver.IsEligibleAsync(Dept, Chief, new SignerEligibility { CertificationTypeIds = { 12 } })).Should().BeFalse("an expired certification does not qualify");
			(await resolver.IsEligibleAsync(Dept, Medic, new SignerEligibility { CertificationTypeIds = { 12 } })).Should().BeFalse("an inactive member never qualifies");
			(await resolver.IsEligibleAsync(Dept, Officer, new SignerEligibility())).Should().BeFalse("an empty rule qualifies nobody");
			(await resolver.GetEligibleUserIdsAsync(Dept, new SignerEligibility { PersonnelRoleIds = { 3 } })).Should().Equal(Officer);
			SignerEligibility.Parse("not json").Should().BeNull();
			SignerEligibility.Parse(new SignerEligibility { CertificationTypeIds = { 12, 12, 4 } }.ToJson()).CertificationTypeIds.Should().Equal(4, 12);
		}

		#endregion

		#region Readiness Pro gate

		[Test]
		public async Task Vendor_acceptance_by_link_needs_Readiness_Pro_and_in_person_is_allowed_without_it()
		{
			_source.Kind = SignatureSubjectKinds.WorkOrderVendorAcceptance;
			_readiness.Setup(x => x.CanUseMaintenanceAsync(Dept)).ReturnsAsync(false);

			(await _service.CheckRemoteAsync(Dept, SignatureSubjectKinds.WorkOrderVendorAcceptance)).Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
			(await CreateAsync(true, SignatureRequestChannels.Remote, ExternalStep("Acme Pumps", 1))).Refusal.Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
			(await _service.CreateAsync(new ExternalSignatureRequestInput
			{
				DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.WorkOrderVendorAcceptance, SubjectId = "wo-1", Channel = SignatureRequestChannels.Remote,
				SignerName = "Acme Pumps", SignerEmail = "vendor@example.com", RequestedByUserId = Requester
			})).Refusal.Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
			_provider.Verify(x => x.CreateEnvelopeAsync(It.IsAny<string>(), It.IsAny<ESignEnvelopeRequest>(), It.IsAny<CancellationToken>()), Times.Never);

			var inPerson = await CreateAsync(true, SignatureRequestChannels.InPerson, ExternalStep("Acme Pumps", 1));
			inPerson.Succeeded.Should().BeTrue();
			ExternalSignatureUse.For(SignatureSubjectKinds.InventoryIssueReceipt).OwningAddon.Should().Be(PlanAddonTypes.ReadinessPro);

			_readiness.Setup(x => x.CanUseMaintenanceAsync(Dept)).ReturnsAsync(true);
			var remote = await CreateAsync(true, SignatureRequestChannels.Remote, MemberStep(Officer, 1), ExternalStep("Acme Pumps", 2));
			remote.Succeeded.Should().BeTrue();
			_provider.Verify(x => x.CreateEnvelopeAsync(It.IsAny<string>(), It.Is<ESignEnvelopeRequest>(e => e.Ordered && e.Recipients.Count == 1 && e.Recipients[0].SigningOrder == 2), It.IsAny<CancellationToken>()), Times.Once);
			_mail.Should().BeEmpty("the vendor's link waits for the member step before it");
			(await SignAsync(remote.Request.SignatureRequestId, Session(Officer))).Succeeded.Should().BeTrue();
			_mail.Should().ContainSingle(m => m.To.Single().Address == "vendor@example.com");
		}

		#endregion

		#region Offline step capture, reminders, Records and payloads

		[Test]
		public async Task An_offline_capture_for_an_ordered_step_is_bound_to_the_request_hash()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, ExternalStep("Pat Recipient", 1, SignatureSignerRoles.Recipient), MemberStep(Officer, 2));
			var id = created.Request.SignatureRequestId;
			OfflineSignatureCapture Capture(string hash) => new OfflineSignatureCapture
			{
				DepartmentId = Dept, SubjectKind = _source.Kind, SubjectId = "eval-1", SignatureRequestId = id, BoundRevisionSha256 = hash, PrintedName = "Pat Recipient",
				SignatureImageDataUrl = Png, CapturedByUserId = Requester, CapturedOnUtc = DateTime.UtcNow.AddMinutes(-30)
			};

			(await _service.CaptureOfflineAsync(Capture(new string('0', 64)))).Refusal.Should().Be(SignatureRequestRefusals.RevisionChanged);
			var accepted = await _service.CaptureOfflineAsync(Capture(_source.Revision));
			accepted.Succeeded.Should().BeTrue();
			accepted.Record.Method.Should().Be((int)SignatureMethods.InPersonOffline);
			accepted.Request.SignatureRequestId.Should().Be(id, "the existing request's step was signed, no new request");
			_requests.Should().ContainSingle();
			_stepNotices.Last().Users.Should().Equal(new[] { Officer }, "the next step's turn started");
		}

		[Test]
		public async Task Reminders_go_to_the_signer_whose_turn_it_is_and_are_claimed_once()
		{
			var created = await CreateAsync(true, SignatureRequestChannels.InPerson, MemberStep(Officer, 1), MemberStep(Chief, 2));
			var officer = _signers.First(s => s.UserId == Officer);
			officer.NextReminderOn.Should().BeCloseTo(DateTime.UtcNow + SignatureReminderPolicy.FirstReminderAfter, TimeSpan.FromMinutes(1));
			_signers.First(s => s.UserId == Chief).NextReminderOn.Should().BeNull("the chief's turn has not started");

			var later = DateTime.UtcNow.AddHours(25);
			var result = await SignaturesLogic.Process(_service, Mock.Of<IDocumentSealingService>(), Mock.Of<IESignWebhookService>(), later);
			result.Reminded.Should().Be(1);
			_stepNotices.Last().Should().Match<(List<string> Users, bool Reminder)>(n => n.Reminder && n.Users.Single() == Officer);
			officer.ReminderCount.Should().Be(1);
			officer.NextReminderOn.Should().BeCloseTo(later + SignatureReminderPolicy.Interval, TimeSpan.FromSeconds(5));

			// Another host holds the claim: nothing is sent twice.
			_claims.Setup(x => x.Claim(It.IsAny<int>(), It.IsAny<int>())).Returns(false);
			officer.NextReminderOn = later;
			(await _service.RemindDueAsync(later.AddMinutes(1), 10)).Should().Be(0);
			_stepNotices.Count(n => n.Reminder).Should().Be(1);
			SignatureReminderPolicy.Next(later, SignatureReminderPolicy.MaxReminders, later.AddDays(30)).Should().BeNull("three reminders at most");
			SignatureReminderPolicy.Next(later, 0, later.AddHours(2)).Should().BeNull("no reminder after the request expires");
		}

		[Test]
		public async Task Records_signatures_are_never_read_or_written_and_workflow_payloads_carry_no_contact()
		{
			// The RmsSignatures repository is a strict mock: any call fails the test.
			_readiness.Setup(x => x.CanUseMaintenanceAsync(Dept)).ReturnsAsync(true);
			_source.Kind = SignatureSubjectKinds.WorkOrderVendorAcceptance;
			var created = await CreateAsync(true, SignatureRequestChannels.Remote, MemberStep(Officer, 1), ExternalStep("Acme Pumps", 2));
			(await SignAsync(created.Request.SignatureRequestId, Session(Officer))).Succeeded.Should().BeTrue();
			await _service.DeclineStepAsync(Dept, created.Request.SignatureRequestId, null, Officer, "x");
			await _service.VoidAsync(Dept, created.Request.SignatureRequestId, Requester, "done");

			_events.Should().NotBeEmpty();
			foreach (var payload in _events.Select(e => JsonConvert.SerializeObject(e)))
				payload.Should().NotContain("vendor@example.com").And.NotContain("5550104040").And.NotContain("Acme").And.NotContain("v***");
		}

		[Test]
		public void Step_rules_are_validated_before_anything_is_stored()
		{
			SignatureOrdering.Validate(null).Should().Be(SignatureRequestRefusals.InvalidRequest);
			var input = new MultiSignerRequestInput { DepartmentId = Dept, SubjectId = "x", RequestedByUserId = Requester };
			SignatureOrdering.Validate(input).Should().Be(SignatureRequestRefusals.InvalidRequest, "a request needs a step");
			input.Steps.Add(new SignatureStepInput { Kind = SignatureSignerKinds.Member, Role = SignatureSignerRoles.Evaluator, Order = 1 });
			SignatureOrdering.Validate(input).Should().Be(SignatureRequestRefusals.InvalidRequest, "a member step names a member or carries a rule");
			input.Steps[0].Eligibility = new SignerEligibility { PersonnelRoleIds = { 4 } };
			SignatureOrdering.Validate(input).Should().Be(SignatureRequestRefusals.None);
			input.Steps[0].Level = SignatureLevels.External;
			SignatureOrdering.Validate(input).Should().Be(SignatureRequestRefusals.InvalidRequest, "a member signs at L1 or L2");
			input.Steps[0].Level = SignatureLevels.StepUp;
			input.ExternalChannel = SignatureRequestChannels.Remote;
			input.Steps.Add(new SignatureStepInput { Kind = SignatureSignerKinds.External, Role = SignatureSignerRoles.Vendor, Order = 2 });
			SignatureOrdering.Validate(input).Should().Be(SignatureRequestRefusals.InvalidRequest, "a remote signer needs a name");
		}

		#endregion
	}
}
