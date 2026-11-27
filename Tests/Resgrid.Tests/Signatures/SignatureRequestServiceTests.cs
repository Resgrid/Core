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
using Resgrid.Services.Signatures;

namespace Resgrid.Tests.Signatures
{
	/// <summary>
	/// e-Signature S2 (L4 external signers): the single-use, expiring signing link; the one-time code before the embedded signer
	/// (the platform's recipient token never leaves the server earlier); in-person capture bound to the revision hash and the
	/// offline capture refused after a revision change; remote signing refused while the owning add-on is inactive (in person
	/// allowed); webhook hints idempotent and state taken only from the re-fetched envelope; RmsSignatures read-only; and
	/// workflow payloads without contact details.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SignatureRequestServiceTests
	{
		private const int Dept = 11;
		private const string Member = "8f8f8f8f-0000-4000-8000-000000000001";
		private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

		private string _baseUrl;
		private List<SignatureRequestRecord> _requests;
		private List<SignatureRequestSigner> _signers;
		private List<SignatureRecord> _records;
		private List<SignatureCapture> _captures;
		private List<ESignWebhookEvent> _hints;
		private List<MailMessage> _mail;
		private List<(string Number, string Body)> _texts;
		private List<SignatureRequestEvent> _events;
		private FakeSource _source;
		private Mock<IESignProvider> _provider;
		private Mock<IESignAccountService> _accounts;
		private Mock<IBusinessOperationsAccessService> _businessOps;
		private Mock<IDocumentSealingService> _sealing;
		private Mock<IRmsSignaturesRepository> _rms;
		private ESignEnvelope _envelope;
		private SignatureRequestService _service;
		private Mock<IDepartmentDataProtectionService> _protection;
		private IProtectedWriteService _protectedWrite;
		private Mock<ISignatureRequestSignersRepository> _signersRepository;

		private sealed class FakeSource : ISignatureSubjectSource
		{
			public SignatureSubjectKinds Kind { get; set; } = SignatureSubjectKinds.TimeReport;
			public string Revision { get; set; } = new string('a', 64);
			public bool Exists { get; set; } = true;
			public int SignedCalls { get; private set; }

			public Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId) =>
				Task.FromResult(Exists ? new SignatureSubjectInfo { Kind = Kind, SubjectId = subjectId, Title = "DTR 7 2026-11-24", RevisionId = "7", RevisionSha256 = Revision, DocumentHtml = "<html><body>DTR</body></html>" } : null);

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
			_hints = new List<ESignWebhookEvent>();
			_mail = new List<MailMessage>();
			_texts = new List<(string, string)>();
			_events = new List<SignatureRequestEvent>();
			_source = new FakeSource();

			var requests = new Mock<ISignatureRequestsRepository>();
			requests.Setup(x => x.InsertAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRequestRecord r, CancellationToken _, bool __) => { r.SignatureRequestId = _requests.Count + 1; _requests.Add(r); return r; });
			requests.Setup(x => x.UpdateAsync(It.IsAny<SignatureRequestRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SignatureRequestRecord r, CancellationToken _, bool __) => r);
			requests.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _requests.FirstOrDefault(r => r.DepartmentId == d && r.SignatureRequestId == id));
			requests.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => _requests.Where(r => r.DepartmentId == d && r.SubjectKind == k && r.SubjectId == s).ToList());
			requests.Setup(x => x.GetByEnvelopeAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int p, string e) => _requests.FirstOrDefault(r => r.ProviderKind == p && r.ProviderEnvelopeId == e));
			requests.Setup(x => x.GetDueExpiryAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync((DateTime now, int take) => _requests.Where(r => r.IsOpen && r.ExpiresOn <= now).ToList());

			var signers = new Mock<ISignatureRequestSignersRepository>();
			signers.Setup(x => x.InsertAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRequestSigner s, CancellationToken _, bool __) => { s.SignatureRequestSignerId = _signers.Count + 100; _signers.Add(s); return s; });
			signers.Setup(x => x.UpdateAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SignatureRequestSigner s, CancellationToken _, bool __) => s);
			signers.Setup(x => x.GetForRequestAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _signers.Where(s => s.DepartmentId == d && s.SignatureRequestId == id).ToList());
			signers.Setup(x => x.GetForRequestsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => _signers.Where(s => s.DepartmentId == d && ids.Contains(s.SignatureRequestId)).ToList());
			signers.Setup(x => x.GetByLinkTokenHashAsync(It.IsAny<string>())).ReturnsAsync((string h) => _signers.FirstOrDefault(s => s.LinkTokenHash == h));
			signers.Setup(x => x.GetBySessionHashAsync(It.IsAny<string>())).ReturnsAsync((string h) => _signers.FirstOrDefault(s => s.SessionHash == h));
			// The conditional update's semantics (the real SQL is proven in SignatureSealingDatabaseTests).
			signers.Setup(x => x.ConsumeLinkTokenAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, string hash, string session, DateTime expires, DateTime now, CancellationToken _) =>
				{
					var s = _signers.FirstOrDefault(x => x.SignatureRequestSignerId == id && x.LinkTokenHash == hash && x.LinkTokenUsedOn == null && x.LinkTokenExpiresOn > now);
					if (s == null) return false;
					s.LinkTokenUsedOn = now; s.SessionHash = session; s.SessionExpiresOn = expires;
					return true;
				});
			// Security review M4: the one-time-code conditional updates (the real SQL, with parallel posts, is proven in SignatureSealingDatabaseTests).
			signers.Setup(x => x.TryCountOtpAttemptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, string hash, int max, DateTime now, CancellationToken _) =>
				{
					var s = _signers.FirstOrDefault(x => x.SignatureRequestSignerId == id && x.OtpHash == hash && x.OtpVerifiedOn == null && x.OtpExpiresOn > now && x.OtpAttempts < max);
					if (s == null) return false;
					s.OtpAttempts++;
					return true;
				});
			signers.Setup(x => x.MarkOtpVerifiedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, string hash, DateTime now, CancellationToken _) =>
				{
					var s = _signers.FirstOrDefault(x => x.SignatureRequestSignerId == id && x.OtpHash == hash && x.OtpVerifiedOn == null);
					if (s == null) return false;
					s.OtpVerifiedOn = now; s.OtpHash = null; s.OtpExpiresOn = null;
					if (s.Status == (int)SignatureSignerStatuses.Pending || s.Status == (int)SignatureSignerStatuses.Opened) s.Status = (int)SignatureSignerStatuses.Verified;
					return true;
				});
			signers.Setup(x => x.WipeExhaustedOtpAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.Callback((int id, string hash, int max, CancellationToken _) =>
				{
					var s = _signers.FirstOrDefault(x => x.SignatureRequestSignerId == id && x.OtpHash == hash && x.OtpVerifiedOn == null && x.OtpAttempts >= max);
					if (s != null) { s.OtpHash = null; s.OtpExpiresOn = null; }
				})
				.Returns(Task.CompletedTask);
			signers.Setup(x => x.IssueOtpAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, string hash, DateTime expires, DateTime sent, int channel, CancellationToken _) =>
				{
					var s = _signers.FirstOrDefault(x => x.SignatureRequestSignerId == id && x.OtpVerifiedOn == null);
					if (s == null) return false;
					s.OtpHash = hash; s.OtpExpiresOn = expires; s.OtpAttempts = 0; s.OtpSentOn = sent; s.OtpChannel = channel;
					return true;
				});
			_signersRepository = signers;

			var records = new Mock<ISignatureRecordsRepository>();
			records.Setup(x => x.InsertAsync(It.IsAny<SignatureRecord>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureRecord r, CancellationToken _, bool __) => { r.SignatureRecordId = _records.Count + 500; _records.Add(r); return r; });
			records.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int k, string s) => _records.Where(r => r.DepartmentId == d && r.SubjectKind == k && r.SubjectId == s).ToList());

			var captures = new Mock<ISignatureCapturesRepository>();
			captures.Setup(x => x.InsertAsync(It.IsAny<SignatureCapture>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SignatureCapture c, CancellationToken _, bool __) => { c.SignatureCaptureId = _captures.Count + 900; _captures.Add(c); return c; });
			captures.Setup(x => x.GetForRequestAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => _captures.Where(c => c.SignatureRequestId == id).ToList());

			var hints = new Mock<IESignWebhookEventsRepository>();
			hints.Setup(x => x.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(() => _hints.Where(h => h.State == (int)ESignWebhookEventStates.Pending).ToList());
			hints.Setup(x => x.ClaimAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string id, DateTime _, DateTime __, CancellationToken ___) => { var h = _hints.First(x => x.ESignWebhookEventId == id); h.State = (int)ESignWebhookEventStates.Processing; h.Attempts++; return true; });
			hints.Setup(x => x.MarkAsync(It.IsAny<string>(), It.IsAny<ESignWebhookEventStates>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string id, ESignWebhookEventStates state, DateTime _, string __, CancellationToken ___) => _hints.First(x => x.ESignWebhookEventId == id).State = (int)state)
				.Returns(Task.CompletedTask);

			_rms = new Mock<IRmsSignaturesRepository>(MockBehavior.Strict);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protectedWrite = null;

			_envelope = new ESignEnvelope
			{
				EnvelopeId = "envelope_1", Status = "PENDING", ItemIds = { "item_1" },
				Recipients = { new ESignRecipient { Reference = "100", ProviderRecipientId = "r1", Email = "cust@example.com", SigningOrder = 1, SigningToken = "recipient-secret-token", SigningStatus = "NOT_SIGNED" } }
			};
			_provider = new Mock<IESignProvider>();
			_provider.SetupGet(x => x.IsAvailable).Returns(true);
			_provider.SetupGet(x => x.EmbedOrigin).Returns("https://sign.test");
			_provider.Setup(x => x.CreateEnvelopeAsync(It.IsAny<string>(), It.IsAny<ESignEnvelopeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _envelope);
			_provider.Setup(x => x.GetEnvelopeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _envelope);
			_provider.Setup(x => x.BuildEmbedUrl(It.IsAny<string>(), It.IsAny<string>())).Returns((string token, string name) => "https://sign.test/embed/sign/" + token);
			_provider.Setup(x => x.DownloadCompletedPdfAsync(It.IsAny<string>(), It.IsAny<ESignEnvelope>(), It.IsAny<CancellationToken>())).ReturnsAsync(Encoding.ASCII.GetBytes("%PDF-1.7 completed"));

			_accounts = new Mock<IESignAccountService>();
			_accounts.Setup(x => x.GetAsync(Dept)).ReturnsAsync(new ESignProviderAccount { DepartmentId = Dept, IsActive = true, ApiTokenEncrypted = "enc" });
			_accounts.Setup(x => x.GetApiTokenAsync(Dept)).ReturnsAsync("api_token");

			_businessOps = new Mock<IBusinessOperationsAccessService>();
			_businessOps.Setup(x => x.HasActiveAddonAsync(Dept)).ReturnsAsync(true);

			_sealing = new Mock<IDocumentSealingService>();
			_sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SealHtmlRequest r, CancellationToken _) => new SealOutcome { Document = new SealedDocument { SealedDocumentId = 77, Status = (int)SealedDocumentStatuses.Pending }, Bytes = new byte[] { 1 } });

			var pdf = new Mock<IPdfProvider>();
			pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Returns(Encoding.ASCII.GetBytes("%PDF-1.4 /Type /Page /Type /Page"));

			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => "enc:" + s);
			encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s.StartsWith("enc:") ? s.Substring(4) : throw new InvalidOperationException());

			var email = new Mock<IEmailSender>();
			email.Setup(x => x.SendEmail(It.IsAny<MailMessage>())).Callback((MailMessage m) => _mail.Add(m)).ReturnsAsync(true);
			var sms = new Mock<ITextMessageProvider>();
			sms.Setup(x => x.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>()))
				.Callback((string n, string b, string d, MobileCarriers c, int dep, bool f, bool call, int max) => _texts.Add((n, b))).ReturnsAsync(true);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Mesa Flats ERT" });
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			var events = new Mock<IEventAggregator>();
			events.Setup(x => x.SendMessage(It.IsAny<SignatureRequestEvent>())).Callback((SignatureRequestEvent e) => _events.Add(e));

			_service = new SignatureRequestService(requests.Object, signers.Object, captures.Object, records.Object, hints.Object, _rms.Object, new[] { (ISignatureSubjectSource)_source },
				_provider.Object, _accounts.Object, _sealing.Object, pdf.Object, encryption.Object, email.Object, sms.Object, departments.Object, Mock.Of<IDepartmentSettingsService>(),
				_businessOps.Object, Mock.Of<IReadinessAccessService>(), Mock.Of<IIncidentBackOfficeAccessService>(), Mock.Of<ILmsAccessService>(), cache.Object, events.Object,
				Mock.Of<ISignatureService>(), Mock.Of<ISignerEligibilityService>(), Mock.Of<ISignatureNotificationService>(), _protection.Object,
				new Lazy<IProtectedWriteService>(() => _protectedWrite ?? Mock.Of<IProtectedWriteService>()));
		}

		[TearDown]
		public void TearDown() => SystemBehaviorConfig.ResgridBaseUrl = _baseUrl;

		private Task<SignatureRequestResult> RemoteAsync(SignatureSignerAuthMethods channel = SignatureSignerAuthMethods.EmailOtp) => _service.CreateAsync(new ExternalSignatureRequestInput
		{
			DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.TimeReport, SubjectId = "dtr-1", Channel = SignatureRequestChannels.Remote, SignerName = "Casey Customer",
			SignerEmail = "cust@example.com", SignerPhone = "+1 555 010 2030", OtpChannel = channel, RequestedByUserId = Member, Culture = "en"
		});

		private Task<SignatureRequestResult> InPersonAsync(SignatureSubjectKinds kind = SignatureSubjectKinds.TimeReport) => _service.CreateAsync(new ExternalSignatureRequestInput
		{
			DepartmentId = Dept, SubjectKind = kind, SubjectId = "dtr-1", Channel = SignatureRequestChannels.InPerson, RequestedByUserId = Member
		});

		private static string CodeFrom(string text) => new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());

		#region Packaging (decision 19)

		[Test]
		public async Task Remote_signing_is_refused_while_the_owning_add_on_is_inactive_and_in_person_is_still_allowed()
		{
			_businessOps.Setup(x => x.HasActiveAddonAsync(Dept)).ReturnsAsync(false);

			var remote = await RemoteAsync();
			var inPerson = await InPersonAsync();

			remote.Refusal.Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
			_provider.Verify(x => x.CreateEnvelopeAsync(It.IsAny<string>(), It.IsAny<ESignEnvelopeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			inPerson.Succeeded.Should().BeTrue();
			inPerson.Request.Channel.Should().Be((int)SignatureRequestChannels.InPerson);
		}

		[Test]
		public async Task Records_documents_have_no_owning_add_on_so_remote_signing_is_never_offered_there()
		{
			_source.Kind = SignatureSubjectKinds.RecordsInspection;
			(await _service.CheckRemoteAsync(Dept, SignatureSubjectKinds.RecordsInspection)).Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
			(await _service.CheckRemoteAsync(Dept, SignatureSubjectKinds.CustodyTransfer)).Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
			(await InPersonAsync(SignatureSubjectKinds.RecordsInspection)).Succeeded.Should().BeTrue();
		}

		[Test]
		public async Task Without_the_platform_or_the_department_token_remote_signing_is_unavailable()
		{
			_provider.SetupGet(x => x.IsAvailable).Returns(false);
			(await _service.CheckRemoteAsync(Dept, SignatureSubjectKinds.TimeReport)).Should().Be(SignatureRequestRefusals.ProviderUnavailable);

			_provider.SetupGet(x => x.IsAvailable).Returns(true);
			_accounts.Setup(x => x.GetAsync(Dept)).ReturnsAsync((ESignProviderAccount)null);
			(await _service.CheckRemoteAsync(Dept, SignatureSubjectKinds.TimeReport)).Should().Be(SignatureRequestRefusals.ProviderUnavailable);
		}

		[Test]
		public async Task An_add_on_check_that_throws_fails_closed()
		{
			_businessOps.Setup(x => x.HasActiveAddonAsync(Dept)).ThrowsAsync(new Exception("billing down"));
			(await _service.CheckRemoteAsync(Dept, SignatureSubjectKinds.Bid)).Should().Be(SignatureRequestRefusals.RemoteNeedsAddon);
		}

		#endregion

		#region The signing link

		[Test]
		public async Task A_remote_request_creates_the_envelope_and_sends_a_link_with_contacts_stored_encrypted_and_masked()
		{
			var result = await RemoteAsync();

			result.Succeeded.Should().BeTrue();
			result.Request.Status.Should().Be((int)SignatureRequestStatuses.Sent);
			result.Request.ProviderEnvelopeId.Should().Be("envelope_1");
			result.Signer.ExternalEmailEncrypted.Should().Be("enc:cust@example.com");
			result.Signer.ContactMasked.Should().Be("c***@example.com");
			result.Signer.ProviderSigningTokenEncrypted.Should().Be("enc:recipient-secret-token");
			result.Signer.LinkTokenHash.Should().Be(SignatureTokens.Hash(result.LinkToken), "only the link token's hash is stored");
			_mail.Single().Body.Should().Contain("https://resgrid.test/Sign/" + result.LinkToken);
			_mail.Single().Body.Should().NotContain("recipient-secret-token", "the platform's token never goes in the message");
		}

		[Test]
		public async Task The_link_is_single_use()
		{
			var result = await RemoteAsync();

			(await _service.OpenLinkAsync(result.LinkToken)).Step.Should().Be(SigningPageSteps.Landing, "a GET (a mail scanner) does not consume it");
			var (first, session) = await _service.StartSessionAsync(result.LinkToken, "203.0.113.5");
			var (second, secondSession) = await _service.StartSessionAsync(result.LinkToken, "203.0.113.5");

			session.Should().NotBeNull();
			first.Step.Should().Be(SigningPageSteps.Consent);
			secondSession.Should().BeNull();
			second.Step.Should().Be(SigningPageSteps.Closed);
			(await _service.OpenLinkAsync(result.LinkToken)).Step.Should().Be(SigningPageSteps.Closed);
		}

		[Test]
		public async Task An_expired_link_is_refused()
		{
			var result = await RemoteAsync();
			result.Signer.LinkTokenExpiresOn = DateTime.UtcNow.AddSeconds(-1);

			(await _service.OpenLinkAsync(result.LinkToken)).Step.Should().Be(SigningPageSteps.Closed);
			(await _service.StartSessionAsync(result.LinkToken, null)).SessionToken.Should().BeNull();
		}

		[Test]
		public async Task A_request_past_its_expiry_closes_its_link_even_if_the_link_itself_is_newer()
		{
			var result = await RemoteAsync();
			result.Request.ExpiresOn = DateTime.UtcNow.AddMinutes(-1);

			(await _service.OpenLinkAsync(result.LinkToken)).Refusal.Should().Be(SignatureRequestRefusals.RequestClosed);
		}

		[Test]
		public async Task A_resent_link_replaces_the_old_one()
		{
			var result = await RemoteAsync();
			var resent = await _service.ResendAsync(Dept, result.Request.SignatureRequestId, Member);

			(await _service.OpenLinkAsync(result.LinkToken)).Step.Should().Be(SigningPageSteps.Closed);
			(await _service.OpenLinkAsync(resent.LinkToken)).Step.Should().Be(SigningPageSteps.Landing);
		}

		[Test]
		public async Task Unknown_or_malformed_tokens_are_closed()
		{
			(await _service.OpenLinkAsync("short")).Step.Should().Be(SigningPageSteps.Closed);
			(await _service.OpenLinkAsync(SignatureTokens.NewToken())).Step.Should().Be(SigningPageSteps.Closed);
			(await _service.GetSessionStateAsync(SignatureTokens.NewToken())).Step.Should().Be(SigningPageSteps.Closed);
		}

		#endregion

		#region Consent and the one-time code before the embed

		[Test]
		public async Task The_embedded_signer_is_only_produced_after_consent_and_a_verified_code()
		{
			var result = await RemoteAsync();
			var (state, session) = await _service.StartSessionAsync(result.LinkToken, "203.0.113.5");
			state.EmbedUrl.Should().BeNull();

			var consented = await _service.ConsentAsync(session, SignatureTokens.ConsentTextVersion, "203.0.113.5");
			consented.Step.Should().Be(SigningPageSteps.Code);
			consented.EmbedUrl.Should().BeNull("consent alone does not open the signer");
			result.Signer.ConsentTextVersion.Should().Be("esign-consent/1");

			var sent = await _service.SendCodeAsync(session, "en");
			sent.CodeSent.Should().BeTrue();
			sent.EmbedUrl.Should().BeNull();
			var code = CodeFrom(_mail.Last().Body.Substring(_mail.Last().Body.IndexOf("is ", StringComparison.Ordinal)));
			code.Should().HaveLength(6);

			var wrong = await _service.VerifyCodeAsync(session, code == "000000" ? "111111" : "000000");
			wrong.Step.Should().Be(SigningPageSteps.Code);
			wrong.EmbedUrl.Should().BeNull();

			var verified = await _service.VerifyCodeAsync(session, code);
			verified.Step.Should().Be(SigningPageSteps.Sign);
			verified.EmbedUrl.Should().Be("https://sign.test/embed/sign/recipient-secret-token");
			result.Signer.OtpHash.Should().BeNull("a used code is wiped");
		}

		[Test]
		public async Task A_code_cannot_be_sent_before_consent()
		{
			var result = await RemoteAsync();
			var (_, session) = await _service.StartSessionAsync(result.LinkToken, null);

			var state = await _service.SendCodeAsync(session, "en");

			state.Step.Should().Be(SigningPageSteps.Consent);
			_mail.Should().HaveCount(1, "only the link was sent");
		}

		[Test]
		public async Task A_wrong_consent_version_is_not_recorded()
		{
			var result = await RemoteAsync();
			var (_, session) = await _service.StartSessionAsync(result.LinkToken, null);

			(await _service.ConsentAsync(session, "esign-consent/0", null)).Step.Should().Be(SigningPageSteps.Consent);
			result.Signer.ConsentedOn.Should().BeNull();
		}

		[Test]
		public async Task Too_many_wrong_codes_wipe_the_code()
		{
			var result = await RemoteAsync();
			var (_, session) = await _service.StartSessionAsync(result.LinkToken, null);
			await _service.ConsentAsync(session, SignatureTokens.ConsentTextVersion, null);
			await _service.SendCodeAsync(session, "en");
			var code = CodeFrom(_mail.Last().Body.Substring(_mail.Last().Body.IndexOf("is ", StringComparison.Ordinal)));

			for (var i = 0; i < ESignConfig.MaxCodeAttempts; i++)
				await _service.VerifyCodeAsync(session, code == "000000" ? "111111" : "000000");

			(await _service.VerifyCodeAsync(session, code)).Step.Should().Be(SigningPageSteps.Code, "the right code no longer works once wiped");
		}

		[Test]
		public async Task Security_review_M4_the_attempt_is_taken_before_the_compare_and_verifying_never_writes_the_whole_row()
		{
			var result = await RemoteAsync();
			var (_, session) = await _service.StartSessionAsync(result.LinkToken, null);
			await _service.ConsentAsync(session, SignatureTokens.ConsentTextVersion, null);
			await _service.SendCodeAsync(session, "en");
			var code = CodeFrom(_mail.Last().Body.Substring(_mail.Last().Body.IndexOf("is ", StringComparison.Ordinal)));
			_signersRepository.Invocations.Clear();

			// Parallel posts used every attempt before this one was read: the right code is not even compared.
			result.Signer.OtpAttempts = ESignConfig.MaxCodeAttempts;
			var refused = await _service.VerifyCodeAsync(session, code);
			refused.Step.Should().Be(SigningPageSteps.Code);
			refused.EmbedUrl.Should().BeNull();
			result.Signer.OtpVerifiedOn.Should().BeNull();
			result.Signer.OtpHash.Should().BeNull("the exhausted code is wiped");

			await _service.SendCodeAsync(session, "en");
			code = CodeFrom(_mail.Last().Body.Substring(_mail.Last().Body.IndexOf("is ", StringComparison.Ordinal)));
			_signersRepository.Invocations.Clear();
			(await _service.VerifyCodeAsync(session, code == "000000" ? "111111" : "000000")).Step.Should().Be(SigningPageSteps.Code);
			(await _service.VerifyCodeAsync(session, code)).Step.Should().Be(SigningPageSteps.Sign);
			result.Signer.OtpAttempts.Should().Be(2, "each post took its attempt first");

			_signersRepository.Verify(x => x.TryCountOtpAttemptAsync(result.Signer.SignatureRequestSignerId, It.IsAny<string>(), ESignConfig.MaxCodeAttempts, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_signersRepository.Verify(x => x.UpdateAsync(It.IsAny<SignatureRequestSigner>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never,
				"a verify post writes only the columns it changes (a stale full row can no longer undo a success)");
		}

		[Test]
		public async Task The_code_goes_by_text_message_when_the_signer_chose_sms()
		{
			var result = await RemoteAsync(SignatureSignerAuthMethods.SmsOtp);
			var (_, session) = await _service.StartSessionAsync(_texts.Count > 0 ? result.LinkToken : result.LinkToken, null);
			await _service.ConsentAsync(session, SignatureTokens.ConsentTextVersion, null);

			(await _service.SendCodeAsync(session, "en")).CodeSent.Should().BeTrue();

			_texts.Should().HaveCount(2, "the link and the code both went by SMS");
			_texts.All(t => t.Number == "+15550102030").Should().BeTrue();
			result.Signer.ContactMasked.Should().Be("*** 2030");
		}

		#endregion

		#region Webhook hints and the re-fetched state

		[Test]
		public async Task Only_the_re_fetched_envelope_changes_state_never_the_hint()
		{
			var result = await RemoteAsync();
			// The hint claims completion; the platform, asked again, says the envelope is still pending.
			_hints.Add(new ESignWebhookEvent { ESignWebhookEventId = "1:evt-1", ProviderKind = 1, ProviderEventId = "evt-1", EventType = "DOCUMENT_COMPLETED", EnvelopeId = "envelope_1", State = 0 });

			(await _service.ProcessWebhookEventsAsync(DateTime.UtcNow, 10)).Should().Be(1);

			result.Request.Status.Should().Be((int)SignatureRequestStatuses.Sent);
			_records.Should().BeEmpty();
			_hints.Single().State.Should().Be((int)ESignWebhookEventStates.Done);
			_provider.Verify(x => x.GetEnvelopeAsync("api_token", "envelope_1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_completed_envelope_records_the_signature_stores_the_provider_pdf_and_seals_the_evidence()
		{
			var result = await RemoteAsync();
			var (_, session) = await _service.StartSessionAsync(result.LinkToken, null);
			await _service.ConsentAsync(session, SignatureTokens.ConsentTextVersion, null);
			await _service.SendCodeAsync(session, "en");
			await _service.VerifyCodeAsync(session, CodeFrom(_mail.Last().Body.Substring(_mail.Last().Body.IndexOf("is ", StringComparison.Ordinal))));
			_envelope.Status = "COMPLETED";
			_envelope.Recipients[0].SigningStatus = "SIGNED";
			_envelope.Recipients[0].SignedAtUtc = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
			SealHtmlRequest sealedRequest = null;
			_sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()))
				.Callback((SealHtmlRequest r, CancellationToken _) => sealedRequest = r)
				.ReturnsAsync(new SealOutcome { Document = new SealedDocument { SealedDocumentId = 9, Status = (int)SealedDocumentStatuses.Sealed }, Bytes = new byte[] { 1 } });

			var state = await _service.CheckCompletionAsync(session);

			state.Step.Should().Be(SigningPageSteps.Done);
			result.Request.Status.Should().Be((int)SignatureRequestStatuses.Completed);
			result.Request.SealedDocumentId.Should().Be(9);
			var record = _records.Single();
			record.Level.Should().Be((int)SignatureLevels.External);
			record.Method.Should().Be((int)SignatureMethods.ExternalLink);
			record.SignerUserId.Should().BeNull();
			record.ExternalSignerName.Should().Be("Casey Customer");
			record.ArtifactSha256.Should().Be(_source.Revision);
			record.StepUpEvidenceReference.Should().StartWith("otp:EmailOtp:");
			_sealing.Verify(x => x.StoreAsIsAsync(Dept, SignatureSubjectKinds.TimeReport, "dtr-1", _source.Revision, result.Request.SignatureRequestId, SealedDocumentRoles.ProviderCompleted,
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
			sealedRequest.Html.Should().Contain(SealingPolicy.Sha256(Encoding.ASCII.GetBytes("%PDF-1.7 completed")), "the evidence binds the platform's completed PDF");
			sealedRequest.Signers.Single().Evidence.Should().Contain("e-mail code to c***@example.com verified");
			_source.SignedCalls.Should().Be(1, "the module write-back runs once");
			_events.Select(e => e.Change).Should().Equal(SignatureRequestChanges.Requested, SignatureRequestChanges.Completed);
		}

		[Test]
		public async Task A_rejected_recipient_declines_the_request()
		{
			var result = await RemoteAsync();
			_envelope.Status = "REJECTED";
			_envelope.Recipients[0].SigningStatus = "REJECTED";
			_envelope.Recipients[0].RejectionReason = "Hours are wrong";

			await _service.ReconcileAsync(Dept, result.Request.SignatureRequestId);

			result.Request.Status.Should().Be((int)SignatureRequestStatuses.Declined);
			result.Request.DeclineReason.Should().Be("Hours are wrong");
			_events.Last().Trigger.Should().Be(WorkflowTriggerEventType.SignatureDeclined);
		}

		[Test]
		public async Task A_hint_for_an_unknown_envelope_is_ignored()
		{
			_hints.Add(new ESignWebhookEvent { ESignWebhookEventId = "1:evt-9", ProviderKind = 1, ProviderEventId = "evt-9", EnvelopeId = "envelope_x", State = 0 });

			await _service.ProcessWebhookEventsAsync(DateTime.UtcNow, 10);

			_hints.Single().State.Should().Be((int)ESignWebhookEventStates.Ignored);
		}

		[Test]
		public async Task A_hint_naming_the_resgrid_external_id_re_fetches_that_request_only_if_it_holds_an_envelope()
		{
			var result = await RemoteAsync();
			_hints.Add(new ESignWebhookEvent { ESignWebhookEventId = "1:evt-2", ProviderKind = 1, ProviderEventId = "evt-2", EnvelopeId = "resgrid:" + Dept + ":" + result.Request.SignatureRequestId, State = 0 });
			_hints.Add(new ESignWebhookEvent { ESignWebhookEventId = "1:evt-3", ProviderKind = 1, ProviderEventId = "evt-3", EnvelopeId = "resgrid:" + (Dept + 1) + ":" + result.Request.SignatureRequestId, State = 0 });

			await _service.ProcessWebhookEventsAsync(DateTime.UtcNow, 10);

			_hints.Single(h => h.ProviderEventId == "evt-2").State.Should().Be((int)ESignWebhookEventStates.Done);
			_hints.Single(h => h.ProviderEventId == "evt-3").State.Should().Be((int)ESignWebhookEventStates.Ignored, "a hint cannot point at another department's request");
		}

		[Test]
		public async Task A_duplicate_hint_is_recorded_once()
		{
			var stored = new HashSet<string>();
			var repository = new Mock<IESignWebhookEventsRepository>();
			repository.Setup(x => x.TryInsertAsync(It.IsAny<ESignWebhookEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync((ESignWebhookEvent e, CancellationToken _) => stored.Add(e.ESignWebhookEventId));
			var webhooks = new ESignWebhookService(repository.Object);

			(await webhooks.RecordHintAsync(ESignProviderKinds.Documenso, "evt-1", "DOCUMENT_SIGNED", "envelope_1")).Should().BeTrue();
			(await webhooks.RecordHintAsync(ESignProviderKinds.Documenso, "evt-1", "DOCUMENT_SIGNED", "envelope_1")).Should().BeFalse();
			stored.Should().Equal("1:evt-1");
		}

		[Test]
		public async Task Expiry_re_fetches_first_then_expires_and_withdraws_the_envelope()
		{
			var result = await RemoteAsync();
			result.Request.ExpiresOn = DateTime.UtcNow.AddMinutes(-1);

			(await _service.ExpireDueAsync(DateTime.UtcNow, 10)).Should().Be(1);

			result.Request.Status.Should().Be((int)SignatureRequestStatuses.Expired);
			_provider.Verify(x => x.DeleteEnvelopeAsync("api_token", "envelope_1", It.IsAny<CancellationToken>()), Times.Once);
			_events.Last().Trigger.Should().Be(WorkflowTriggerEventType.SignatureExpired);
		}

		#endregion

		#region In person and offline

		[Test]
		public async Task An_in_person_capture_is_bound_to_the_revision_hash_it_was_shown()
		{
			var request = (await InPersonAsync()).Request;

			var stale = await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = request.SignatureRequestId, BoundRevisionSha256 = new string('f', 64), PrintedName = "Casey Customer",
				SignatureImageDataUrl = Png, ConsentGiven = true, CapturedByUserId = Member
			});
			stale.Refusal.Should().Be(SignatureRequestRefusals.RevisionChanged);

			var signed = await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = request.SignatureRequestId, BoundRevisionSha256 = request.ArtifactSha256, PrintedName = "Casey Customer",
				SignatureImageDataUrl = Png, WitnessName = "Wes Witness", ConsentGiven = true, CapturedByUserId = Member
			});

			signed.Succeeded.Should().BeTrue();
			signed.Record.Method.Should().Be((int)SignatureMethods.InPersonOnline);
			signed.Record.Level.Should().Be((int)SignatureLevels.External);
			signed.Record.ArtifactSha256.Should().Be(_source.Revision);
			signed.Record.CapturedByUserId.Should().Be(Member);
			_captures.Single().BoundRevisionSha256.Should().Be(_source.Revision);
			_captures.Single().SignatureImageEncrypted.Should().StartWith("enc:", "the drawn signature is encrypted at rest");
			request.Status.Should().Be((int)SignatureRequestStatuses.Completed);
		}

		[Test]
		public async Task Adp_a_records_signers_name_is_sealed_and_never_reaches_the_evidence_the_snapshot_or_a_screen()
		{
			// Round 8, catalog 39: the occupant representative of a Records inspection is the person RmsInspections.SignatureName protects.
			using var kit = new Resgrid.Tests.Adp.AdpTestKit(departmentId: Dept);
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(true);
			_protectedWrite = kit.Seam;
			SealHtmlRequest evidence = null;
			_sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>())).Callback((SealHtmlRequest r, CancellationToken _) => evidence = r)
				.ReturnsAsync(new SealOutcome { Document = new SealedDocument { SealedDocumentId = 77, Status = (int)SealedDocumentStatuses.Pending }, Bytes = new byte[] { 1 } });
			_source.Kind = SignatureSubjectKinds.RecordsInspection;
			var request = (await InPersonAsync(SignatureSubjectKinds.RecordsInspection)).Request;

			var signed = await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = request.SignatureRequestId, BoundRevisionSha256 = request.ArtifactSha256, PrintedName = "SYNTHETIC-OCCUPANT-CANARY",
				SignatureImageDataUrl = Png, WitnessName = "SYNTHETIC-WITNESS-CANARY", ConsentGiven = true, CapturedByUserId = Member
			});

			signed.Succeeded.Should().BeTrue("a protected department signs exactly as any other");
			var signer = _signers.Single(s => s.SignatureRequestId == request.SignatureRequestId);
			ProtectedDataEnvelope.HasEnvelopePrefix(signer.ExternalName).Should().BeTrue();
			signer.ExternalNameForDisplay.Should().Be(ProtectedDataEnvelope.RedactionValue);
			var record = _records.Single();
			ProtectedDataEnvelope.HasEnvelopePrefix(record.ExternalSignerName).Should().BeTrue();
			record.SignerNameSnapshot.Should().BeNull("the snapshot the lists read without a grant is never a copy of the protected name");
			ProtectedDataEnvelope.HasEnvelopePrefix(_captures.Single().PrintedNameEncrypted).Should().BeTrue();
			ProtectedDataEnvelope.HasEnvelopePrefix(_captures.Single().WitnessNameEncrypted).Should().BeTrue();
			kit.Open("signaturerequestsigners.externalname", signer.SignatureRequestSignerId.ToString(), signer.ExternalName).Should().Be("SYNTHETIC-OCCUPANT-CANARY",
				"the name is kept, sealed, and revealable with the step-up flow");

			evidence.Should().NotBeNull();
			evidence.Html.Should().NotContain("CANARY").And.NotContain("rgdp:");
			evidence.Signers.Should().OnlyContain(s => s.Name == SignatureRequestService.ProtectedSignerName && (s.Evidence == null || !s.Evidence.Contains("CANARY")));
			kit.Decrypted.Should().BeEmpty("the unattended evidence never decrypts");
		}

		[Test]
		public async Task Adp_a_customer_signers_name_is_never_encrypted_even_in_a_protected_department()
		{
			using var kit = new Resgrid.Tests.Adp.AdpTestKit(departmentId: Dept);
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(true);
			_protectedWrite = kit.Seam;
			var request = (await InPersonAsync()).Request;

			(await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = request.SignatureRequestId, BoundRevisionSha256 = request.ArtifactSha256, PrintedName = "Casey Customer",
				SignatureImageDataUrl = Png, ConsentGiven = true, CapturedByUserId = Member
			})).Succeeded.Should().BeTrue();

			_signers.Single().ExternalName.Should().Be("Casey Customer", "customer-facing documents are never under ADP");
			_records.Single().SignerNameSnapshot.Should().Be("Casey Customer");
			kit.Broker.Verify(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_in_person_capture_is_refused_when_the_subject_changed_after_the_request()
		{
			var request = (await InPersonAsync()).Request;
			_source.Revision = new string('c', 64);

			var result = await _service.CaptureInPersonAsync(new InPersonSignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = request.SignatureRequestId, BoundRevisionSha256 = request.ArtifactSha256, PrintedName = "Casey",
				SignatureImageDataUrl = Png, ConsentGiven = true, CapturedByUserId = Member
			});

			result.Refusal.Should().Be(SignatureRequestRefusals.RevisionChanged);
			_records.Should().BeEmpty();
		}

		[Test]
		public async Task An_in_person_capture_needs_consent_a_name_and_a_drawn_png()
		{
			var request = (await InPersonAsync()).Request;
			var capture = new InPersonSignatureCapture { DepartmentId = Dept, SignatureRequestId = request.SignatureRequestId, BoundRevisionSha256 = request.ArtifactSha256, PrintedName = "Casey", SignatureImageDataUrl = Png, CapturedByUserId = Member };

			(await _service.CaptureInPersonAsync(capture)).Refusal.Should().Be(SignatureRequestRefusals.InvalidRequest, "no consent");
			capture.ConsentGiven = true;
			capture.SignatureImageDataUrl = "data:image/png;base64,QUJD";
			(await _service.CaptureInPersonAsync(capture)).Refusal.Should().Be(SignatureRequestRefusals.InvalidRequest, "not a PNG");
			capture.SignatureImageDataUrl = Png;
			capture.PrintedName = " ";
			(await _service.CaptureInPersonAsync(capture)).Refusal.Should().Be(SignatureRequestRefusals.InvalidRequest, "no name");
		}

		[Test]
		public async Task An_offline_capture_is_accepted_at_the_bound_revision_and_refused_after_a_revision_change()
		{
			var capture = new OfflineSignatureCapture
			{
				DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.TimeReport, SubjectId = "dtr-1", BoundRevisionSha256 = _source.Revision, PrintedName = "Casey Customer",
				SignatureImageDataUrl = Png, CapturedOnUtc = DateTime.UtcNow.AddHours(-3), DeviceInfo = "Omni iOS", CapturedByUserId = Member
			};

			var accepted = await _service.CaptureOfflineAsync(capture);
			accepted.Succeeded.Should().BeTrue();
			accepted.Record.Method.Should().Be((int)SignatureMethods.InPersonOffline);
			accepted.Request.Status.Should().Be((int)SignatureRequestStatuses.Completed);

			_source.Revision = new string('d', 64);
			var refused = await _service.CaptureOfflineAsync(capture);
			refused.Refusal.Should().Be(SignatureRequestRefusals.RevisionChanged);
			_records.Should().HaveCount(1, "nothing is recorded for the changed revision");
		}

		#endregion

		#region Records signatures and workflow payloads

		[Test]
		public async Task Records_signatures_are_projected_read_only()
		{
			_rms.Setup(x => x.GetForRecordAsync(Dept, "rec-1")).ReturnsAsync(new[]
			{
				new RmsSignature { RmsSignatureId = "s1", DepartmentId = Dept, RecordId = "rec-1", RevisionId = "rev-2", SignerUserId = Member, SignerNameSnapshot = "Pat Officer",
					Intent = (int)RmsSignatureIntent.Approval, StatementText = "protected-ciphertext", IsProtected = true, ArtifactChecksum = new string('e', 64), SignedOn = DateTime.UtcNow },
				new RmsSignature { RmsSignatureId = "s2", DepartmentId = Dept + 1, RecordId = "rec-1" }
			});

			var projected = await _service.GetRecordSignaturesProjectionAsync(Dept, "rec-1");

			projected.Should().HaveCount(1, "another department's row is never shown");
			projected[0].Record.SubjectKind.Should().Be((int)SignatureSubjectKinds.RecordsDocument);
			projected[0].Record.Intent.Should().Be((int)SignatureIntents.Approve);
			projected[0].Record.StatementText.Should().BeNull("a protected statement is never revealed by the projection");
			projected[0].State.Should().Be(SignatureVerificationStates.Valid);
			// Strict mock: the projection only read; any write to RmsSignatures would have thrown.
			_rms.Verify(x => x.GetForRecordAsync(Dept, "rec-1"), Times.Once);
			_rms.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Workflow_payloads_carry_no_signer_contact_details()
		{
			var result = await RemoteAsync(SignatureSignerAuthMethods.SmsOtp);
			var payload = JsonConvert.SerializeObject(_events.Single());

			payload.Should().NotContain("cust@example.com").And.NotContain("5550102030").And.NotContain("Casey").And.NotContain("c***").And.NotContain("*** 2030");
			payload.Should().Contain("dtr-1").And.Contain("TimeReport");
			typeof(SignatureRequestEvent).GetProperties().Select(p => p.Name).Should()
				.NotContain(n => n.Contains("Email") || n.Contains("Phone") || n.Contains("Contact") || n.Contains("SignerName") || n.Contains("ExternalName"));
			_events.Single().Trigger.Should().Be(WorkflowTriggerEventType.SignatureRequested);
			result.Succeeded.Should().BeTrue();
		}

		[TestCase(SignatureRequestChanges.Requested, WorkflowTriggerEventType.SignatureRequested, 220)]
		[TestCase(SignatureRequestChanges.Completed, WorkflowTriggerEventType.SignatureCompleted, 221)]
		[TestCase(SignatureRequestChanges.Declined, WorkflowTriggerEventType.SignatureDeclined, 222)]
		[TestCase(SignatureRequestChanges.Expired, WorkflowTriggerEventType.SignatureExpired, 223)]
		public void Each_change_maps_to_its_allocated_trigger(SignatureRequestChanges change, WorkflowTriggerEventType trigger, int value)
		{
			new SignatureRequestEvent { Change = change }.Trigger.Should().Be(trigger);
			((int)trigger).Should().Be(value);
		}

		[Test]
		public void Phone_and_email_are_normalized_or_refused()
		{
			SignatureRequestService.NormalizePhone("+1 (555) 010-2030").Should().Be("+15550102030");
			SignatureRequestService.NormalizePhone("555 010 2030").Should().BeNull("international format is required");
			SignatureRequestService.NormalizeEmail("cust@example.com").Should().Be("cust@example.com");
			SignatureRequestService.NormalizeEmail("Casey <cust@example.com>").Should().BeNull();
		}

		#endregion
	}
}
