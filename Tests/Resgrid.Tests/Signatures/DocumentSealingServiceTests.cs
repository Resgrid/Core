using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Signatures;

namespace Resgrid.Tests.Signatures
{
	/// <summary>
	/// e-Signature S1 (L3 sealing): fails closed (nothing configured ⇒ "sealing pending", input kept, no local-time seal), the
	/// worker retries with backoff, a result without a timestamp is refused, B-LTA by retention, the audit page's content,
	/// re-download reuses the sealed copy (earlier documents are sealed on their next download), and "Verify a document"
	/// binds to the exact bytes (a tampered PDF fails DSS validation and is not "issued by Resgrid").
	/// </summary>
	[TestFixture, NonParallelizable]
	public class DocumentSealingServiceTests
	{
		private const int Dept = 7;
		private static readonly string Revision = new string('a', 64);
		private static readonly byte[] RenderedPdf = Encoding.ASCII.GetBytes("%PDF-1.4 rendered");
		private static readonly byte[] SealedPdf = Encoding.ASCII.GetBytes("%PDF-1.4 rendered + PAdES seal");

		private string _tsa;
		private List<SealedDocument> _documents;
		private List<SealedDocumentInput> _inputs;
		private Dictionary<string, byte[]> _objects;
		private Mock<IDssClient> _dss;
		private Mock<ISigningKeyProvider> _keys;
		private Mock<ISealedDocumentStore> _store;
		private Mock<IPdfProvider> _pdf;
		private Mock<IDepartmentDataProtectionService> _protection;
		private DocumentSealingService _service;
		private List<Resgrid.Model.Events.DocumentSealingWorkflowEvent> _sealingEvents;

		[SetUp]
		public void SetUp()
		{
			_tsa = SealingConfig.TsaUrl;
			SealingConfig.TsaUrl = "http://tsa.test";
			_documents = new List<SealedDocument>();
			_inputs = new List<SealedDocumentInput>();
			_objects = new Dictionary<string, byte[]>();
			_protection = new Mock<IDepartmentDataProtectionService>();

			var documents = new Mock<ISealedDocumentsRepository>();
			documents.Setup(x => x.InsertAsync(It.IsAny<SealedDocument>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SealedDocument d, CancellationToken _, bool __) => { d.SealedDocumentId = _documents.Count + 1; _documents.Add(d); return d; });
			documents.Setup(x => x.UpdateAsync(It.IsAny<SealedDocument>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SealedDocument d, CancellationToken _, bool __) => d);
			documents.Setup(x => x.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int dept, int id) => _documents.FirstOrDefault(d => d.DepartmentId == dept && d.SealedDocumentId == id));
			documents.Setup(x => x.GetForSubjectAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int dept, int kind, string subject, int role, string revision) => _documents
					.Where(d => d.DepartmentId == dept && d.SubjectKind == kind && d.SubjectId == subject && d.Role == role && (string.IsNullOrEmpty(revision) || d.RevisionSha256 == revision)).ToList());
			documents.Setup(x => x.GetBySha256Async(It.IsAny<string>()))
				.ReturnsAsync((string sha) => _documents.Where(d => d.Sha256 == sha).ToList());
			documents.Setup(x => x.GetDuePendingAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync((DateTime now, DateTime stale, int take) => _documents.Where(d => d.Status == (int)SealedDocumentStatuses.Pending && (d.NextAttemptOn == null || d.NextAttemptOn <= now)).Take(take).ToList());
			documents.Setup(x => x.ClaimAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var inputs = new Mock<ISealedDocumentInputsRepository>();
			inputs.Setup(x => x.InsertAsync(It.IsAny<SealedDocumentInput>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SealedDocumentInput i, CancellationToken _, bool __) => { _inputs.Add(i); return i; });
			inputs.Setup(x => x.GetForDocumentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int dept, int id) => _inputs.FirstOrDefault(i => i.DepartmentId == dept && i.SealedDocumentId == id));
			inputs.Setup(x => x.DeleteForDocumentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int dept, int id, CancellationToken _) => _inputs.RemoveAll(i => i.SealedDocumentId == id));

			_dss = new Mock<IDssClient>();
			_dss.SetupGet(x => x.IsConfigured).Returns(false);
			_keys = new Mock<ISigningKeyProvider>();
			_keys.SetupGet(x => x.IsConfigured).Returns(false);
			_store = new Mock<ISealedDocumentStore>();
			_store.SetupGet(x => x.IsConfigured).Returns(false);
			_store.Setup(x => x.PutAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string key, byte[] bytes, string type, CancellationToken _) => _objects[key] = bytes).Returns(Task.CompletedTask);
			_store.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string key, CancellationToken _) => _objects.TryGetValue(key, out var b) ? b : null);
			_pdf = new Mock<IPdfProvider>();
			_pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Returns(RenderedPdf);

			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => "enc:" + s);
			encryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s.Substring(4));

			_sealingEvents = new List<Resgrid.Model.Events.DocumentSealingWorkflowEvent>();
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage(It.IsAny<Resgrid.Model.Events.DocumentSealingWorkflowEvent>())).Callback((Resgrid.Model.Events.DocumentSealingWorkflowEvent e) => _sealingEvents.Add(e));
			_service = new DocumentSealingService(documents.Object, inputs.Object, _dss.Object, _keys.Object, _store.Object, _pdf.Object, encryption.Object, events.Object, _protection.Object);
		}

		[TearDown]
		public void TearDown() => SealingConfig.TsaUrl = _tsa;

		private void ConfigureSealing(bool withTimestamp = true)
		{
			_dss.SetupGet(x => x.IsConfigured).Returns(true);
			_keys.SetupGet(x => x.IsConfigured).Returns(true);
			_store.SetupGet(x => x.IsConfigured).Returns(true);
			_keys.Setup(x => x.GetCertificateAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SigningCertificateInfo { Certificate = new byte[] { 1 }, Thumbprint = "thumb", KeyAlgorithm = "RSA" });
			_keys.Setup(x => x.SignDigestAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 9, 9 });
			_dss.Setup(x => x.GetDataToSignAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new byte[] { 1, 2, 3 });
			_dss.Setup(x => x.SignDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(SealedPdf);
			_dss.Setup(x => x.ValidateAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DssValidationReport
				{
					Completed = true, SignaturesCount = 1, ValidSignaturesCount = 1,
					Signatures = { new DssSignatureResult { Indication = "TOTAL_PASSED", SignatureFormat = withTimestamp ? "PAdES-BASELINE-T" : "PAdES-BASELINE-B",
						TimestampTime = withTimestamp ? new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc) : (DateTime?)null, TimestampAuthority = withTimestamp ? "Test TSA" : null } }
				});
		}

		private static SealHtmlRequest Request(int? retention = null) => new SealHtmlRequest
		{
			DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.TrainingCertificate, SubjectId = "42", RevisionSha256 = Revision, Title = "CPR",
			FileName = "certificate-42.pdf", Html = "<html><body><p>Certificate</p></body></html>", PageSize = "Letter", RetentionYears = retention,
			Signers = { new SealAuditSigner { Name = "Pat Officer", Role = "Verifier", Level = 2, Method = "StepUp", SignedOnUtc = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc), Evidence = "e-mail code to p***@example.com verified" } }
		};

		[Test]
		public async Task Nothing_configured_leaves_the_document_signed_sealing_pending_with_its_input_kept()
		{
			var outcome = await _service.SealHtmlAsync(Request());

			outcome.Sealed.Should().BeFalse();
			outcome.Pending.Should().BeTrue();
			outcome.Bytes.Should().Equal(RenderedPdf, "the member still gets the document, unsealed");
			outcome.Reason.Should().Be("sealing_sidecar_not_configured");
			_documents.Single().Status.Should().Be((int)SealedDocumentStatuses.Pending);
			_documents.Single().NextAttemptOn.Should().NotBeNull();
			_inputs.Single().Html.Should().StartWith("enc:", "the pending input waits encrypted");
			_dss.Verify(x => x.SignDocumentAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Any_missing_part_fails_closed_and_is_named()
		{
			ConfigureSealing();
			SealingConfig.TsaUrl = "";
			(await _service.SealHtmlAsync(Request())).Reason.Should().Be("sealing_tsa_not_configured");

			SealingConfig.TsaUrl = "http://tsa.test";
			_keys.SetupGet(x => x.IsConfigured).Returns(false);
			_service.GetAvailability().MissingPart.Should().Be("sealing_key_not_configured");
			_keys.SetupGet(x => x.IsConfigured).Returns(true);
			_store.SetupGet(x => x.IsConfigured).Returns(false);
			_service.GetAvailability().MissingPart.Should().Be("sealing_store_not_configured");
		}

		[Test]
		public async Task A_repeated_download_while_pending_does_not_queue_the_document_twice()
		{
			await _service.SealHtmlAsync(Request());
			await _service.SealHtmlAsync(Request());

			_documents.Should().HaveCount(1);
			_inputs.Should().HaveCount(1);
		}

		[Test]
		public async Task The_worker_does_nothing_and_counts_no_attempt_while_sealing_is_not_configured()
		{
			await _service.SealHtmlAsync(Request());
			_documents.Single().NextAttemptOn = DateTime.UtcNow.AddMinutes(-1);

			(await _service.RetryPendingAsync(DateTime.UtcNow, 10)).Should().Be(0);
			_documents.Single().Attempts.Should().Be(0, "a department waiting for its operator is never pushed towards Failed");
			_documents.Single().Status.Should().Be((int)SealedDocumentStatuses.Pending);
		}

		[Test]
		public async Task The_worker_seals_a_pending_document_once_sealing_is_configured()
		{
			await _service.SealHtmlAsync(Request());
			_documents.Single().NextAttemptOn = DateTime.UtcNow.AddMinutes(-1);
			ConfigureSealing();

			(await _service.RetryPendingAsync(DateTime.UtcNow, 10)).Should().Be(1);

			var document = _documents.Single();
			document.Status.Should().Be((int)SealedDocumentStatuses.Sealed);
			document.Sha256.Should().Be(SealingPolicy.Sha256(SealedPdf));
			document.SigningCertThumbprint.Should().Be("thumb");
			document.TimestampedOn.Should().Be(new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc));
			_inputs.Should().BeEmpty("the input is deleted once sealed");
			_objects[document.StorageKey].Should().Equal(SealedPdf);
			// Round 7C, workflow trigger 218: once, after the sealed state is stored.
			_sealingEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.DocumentSealed && e.SealedDocumentId == document.SealedDocumentId && e.Status == "Sealed");
		}

		[Test]
		public async Task A_sidecar_failure_backs_off_and_the_document_stays_pending()
		{
			await _service.SealHtmlAsync(Request());
			ConfigureSealing();
			_documents.Single().NextAttemptOn = DateTime.UtcNow.AddMinutes(-1);
			_dss.Setup(x => x.GetDataToSignAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("sealing_sidecar_http_503"));

			var before = DateTime.UtcNow;
			(await _service.RetryPendingAsync(DateTime.UtcNow, 10)).Should().Be(0);

			var document = _documents.Single();
			document.Status.Should().Be((int)SealedDocumentStatuses.Pending);
			document.Attempts.Should().Be(1);
			document.LastError.Should().Be("sealing_sidecar_http_503");
			_sealingEvents.Should().BeEmpty("a retry still to come is neither sealed nor failed");
			document.NextAttemptOn.Should().BeAfter(before.Add(SealingPolicy.Backoff(1)).AddSeconds(-5));
			_inputs.Should().HaveCount(1, "the input is kept for the next attempt");
		}

		[Test]
		public async Task The_last_attempt_marks_the_document_failed()
		{
			await _service.SealHtmlAsync(Request());
			ConfigureSealing();
			var document = _documents.Single();
			document.Attempts = SealingPolicy.MaxAttempts - 1;
			document.NextAttemptOn = DateTime.UtcNow.AddMinutes(-1);
			_dss.Setup(x => x.GetDataToSignAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("boom"));

			await _service.RetryPendingAsync(DateTime.UtcNow, 10);

			document.Status.Should().Be((int)SealedDocumentStatuses.Failed);
			document.NextAttemptOn.Should().BeNull();
			// Round 7C, workflow trigger 219: the final failure only, with a code and never the raw error text.
			_sealingEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.DocumentSealingFailed && e.ErrorCode == "boom");
		}

		[Test]
		public async Task A_sealed_result_without_a_timestamp_is_refused_never_kept_with_a_local_time()
		{
			ConfigureSealing(withTimestamp: false);

			var outcome = await _service.SealHtmlAsync(Request());

			outcome.Sealed.Should().BeFalse();
			outcome.Bytes.Should().Equal(RenderedPdf, "the unsealed rendering is handed out, not the timestamp-less seal");
			_documents.Single().Status.Should().Be((int)SealedDocumentStatuses.Pending);
			_documents.Single().LastError.Should().Be("sealing_no_timestamp");
			_objects.Should().BeEmpty("nothing without a timestamp is stored as sealed");
		}

		[Test]
		public async Task Sealing_now_uses_the_two_step_flow_and_signs_only_a_digest()
		{
			ConfigureSealing();
			byte[] digest = null;
			_keys.Setup(x => x.SignDigestAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.Callback((byte[] d, CancellationToken _) => digest = d).ReturnsAsync(new byte[] { 9 });

			var outcome = await _service.SealHtmlAsync(Request());

			outcome.Sealed.Should().BeTrue();
			outcome.Bytes.Should().Equal(SealedPdf);
			digest.Should().Equal(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 }), "the key provider signs the SHA-256 of DSS's data to sign");
		}

		[TestCase(null, false, PadesLevels.BaselineT)]
		[TestCase(0, false, PadesLevels.BaselineT)]
		[TestCase(null, true, PadesLevels.BaselineLT)]
		[TestCase(7, false, PadesLevels.BaselineLT)]
		[TestCase(10, false, PadesLevels.BaselineLT)]
		[TestCase(11, false, PadesLevels.BaselineLTA)]
		[TestCase(30, true, PadesLevels.BaselineLTA)]
		public void The_level_follows_retention(int? years, bool longTerm, PadesLevels expected) =>
			SealingPolicy.LevelFor(years, longTerm).Should().Be(expected);

		[Test]
		public async Task A_subject_kept_more_than_ten_years_is_sealed_at_B_LTA_and_scheduled_for_re_timestamping()
		{
			ConfigureSealing();
			string level = null;
			_dss.Setup(x => x.GetDataToSignAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.Callback((byte[] _, string __, string l, SigningCertificateInfo ___, long ____, CancellationToken _____) => level = l).ReturnsAsync(new byte[] { 1 });

			await _service.SealHtmlAsync(Request(retention: 25));

			level.Should().Be("PAdES_BASELINE_LTA");
			_documents.Single().PadesLevel.Should().Be((int)PadesLevels.BaselineLTA);
			_documents.Single().RetimestampDueOn.Should().NotBeNull();
		}

		[Test]
		public void The_audit_page_names_the_subject_revision_hash_signers_levels_times_and_code_evidence()
		{
			var request = Request();
			request.DepartmentName = "Station 9 FD";

			var page = SealingAuditPage.Build(request, "en");

			page.Should().Contain("page-break-before:always");
			page.Should().Contain("Station 9 FD");
			page.Should().Contain("CPR");
			page.Should().Contain("TrainingCertificate").And.Contain("42");
			page.Should().Contain(Revision).And.Contain(SignaturePolicy.ShortCode(Revision));
			page.Should().Contain("Pat Officer").And.Contain("L2").And.Contain("StepUp");
			page.Should().Contain("2026-11-20 10:00:00 UTC");
			page.Should().Contain("p***@example.com verified");
		}

		[Test]
		public void The_audit_page_is_appended_inside_the_body_and_encodes_what_it_prints()
		{
			var request = Request();
			request.Signers[0].Name = "<script>x</script>";

			var html = SealingAuditPage.Append("<html><body><p>doc</p></body></html>", request, "en");

			html.Should().EndWith("</body></html>");
			html.IndexOf(SealingAuditPage.Marker, StringComparison.Ordinal).Should().BeGreaterThan(html.IndexOf("<p>doc</p>", StringComparison.Ordinal));
			html.Should().NotContain("<script>x</script>").And.Contain("&lt;script&gt;");
		}

		[Test]
		public async Task A_re_download_of_the_same_revision_serves_the_sealed_copy_without_sealing_again()
		{
			ConfigureSealing();
			await _service.SealHtmlAsync(Request());
			_dss.Invocations.Clear();

			var again = await _service.SealHtmlAsync(Request());

			again.Sealed.Should().BeTrue();
			again.Bytes.Should().Equal(SealedPdf);
			_documents.Should().HaveCount(1);
			_dss.Verify(x => x.GetDataToSignAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SigningCertificateInfo>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_document_issued_before_sealing_existed_is_sealed_on_its_next_download()
		{
			// R-S3: a wave-2 certificate has no sealed row; its first download after S1 is configured seals it.
			ConfigureSealing();

			var outcome = await _service.SealHtmlAsync(Request());

			outcome.Sealed.Should().BeTrue();
			_documents.Single().AuditPageIncluded.Should().BeTrue();
		}

		[Test]
		public async Task A_changed_revision_is_a_new_document()
		{
			ConfigureSealing();
			await _service.SealHtmlAsync(Request());
			var changed = Request();
			changed.RevisionSha256 = new string('b', 64);

			await _service.SealHtmlAsync(changed);

			_documents.Should().HaveCount(2);
		}

		[Test]
		public async Task Verify_matches_the_exact_bytes_and_a_tampered_copy_fails_validation()
		{
			ConfigureSealing();
			var sealedOutcome = await _service.SealHtmlAsync(Request());
			var tampered = sealedOutcome.Bytes.ToArray();
			tampered[tampered.Length - 1] ^= 0x01;
			_dss.Setup(x => x.ValidateAsync(It.Is<byte[]>(b => b.SequenceEqual(tampered)), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DssValidationReport { Completed = true, SignaturesCount = 1, ValidSignaturesCount = 0, Signatures = { new DssSignatureResult { Indication = "TOTAL_FAILED", SubIndication = "HASH_FAILURE", SignatureFormat = "PAdES-BASELINE-T" } } });

			var genuine = await _service.VerifyAsync(Dept, sealedOutcome.Bytes);
			var forged = await _service.VerifyAsync(Dept, tampered);

			genuine.IssuedByResgrid.Should().BeTrue();
			genuine.IssuedByThisDepartment.Should().BeTrue();
			genuine.Validation.AllPassed.Should().BeTrue();
			forged.IssuedByResgrid.Should().BeFalse("one changed byte is not the document Resgrid issued");
			forged.Validation.AllPassed.Should().BeFalse();
			forged.Validation.Signatures.Single().SubIndication.Should().Be("HASH_FAILURE");
		}

		[Test]
		public async Task Verify_by_another_department_confirms_issue_without_naming_the_subject()
		{
			ConfigureSealing();
			var outcome = await _service.SealHtmlAsync(Request());

			var other = await _service.VerifyAsync(Dept + 1, outcome.Bytes);

			other.IssuedByResgrid.Should().BeTrue();
			other.IssuedByThisDepartment.Should().BeFalse();
			other.IssuedDocument.SubjectId.Should().BeNull();
			other.IssuedDocument.Title.Should().BeNull();
		}

		[Test]
		public async Task A_module_pdf_is_sealed_as_given_without_an_audit_page()
		{
			ConfigureSealing();

			var outcome = await _service.SealPdfAsync(new SealPdfRequest
			{
				DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.RecordsDocument, SubjectId = "rec-1", Pdf = RenderedPdf, LongTermSubject = true
			});

			outcome.Sealed.Should().BeTrue();
			_documents.Single().AuditPageIncluded.Should().BeFalse();
			_documents.Single().RevisionSha256.Should().Be(SealingPolicy.Sha256(RenderedPdf));
			_documents.Single().PadesLevel.Should().Be((int)PadesLevels.BaselineLT);
		}

		[Test]
		public async Task Adp_a_protected_departments_records_export_is_sealed_and_handed_back_but_never_kept()
		{
			// Round 8: the sealed copy of a Records revision or disclosure carries the values the member revealed. ADP's export rule is that a
			// stored copy inherits its source's protection; the sealed store keeps plain PDFs that unattended steps read, so nothing is kept.
			ConfigureSealing();
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(true);

			foreach (var kind in new[] { SignatureSubjectKinds.RecordsDocument, SignatureSubjectKinds.RecordsDisclosure })
			{
				var outcome = await _service.SealPdfAsync(new SealPdfRequest { DepartmentId = Dept, SubjectKind = kind, SubjectId = "rec-" + (int)kind, Pdf = RenderedPdf, LongTermSubject = true });

				outcome.Sealed.Should().BeTrue();
				outcome.Bytes.Should().Equal(SealedPdf, "the member gets the sealed file");
				outcome.Document.StorageKey.Should().BeNull();
				outcome.Document.Sha256.Should().Be(SealingPolicy.Sha256(SealedPdf), "the hash stays so the copy verifies as issued");
				outcome.Document.RetimestampDueOn.Should().BeNull("an unattended re-timestamp would read the plain copy");
			}

			_objects.Should().BeEmpty("no sealed PDF reaches the store");
			_inputs.Should().BeEmpty("no input is queued for a retry");
			_store.Verify(x => x.PutAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Adp_a_protected_departments_records_export_keeps_nothing_when_sealing_is_unavailable()
		{
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(true);

			var outcome = await _service.SealPdfAsync(new SealPdfRequest { DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.RecordsDocument, SubjectId = "rec-1", Pdf = RenderedPdf });

			outcome.Sealed.Should().BeFalse();
			outcome.Bytes.Should().Equal(RenderedPdf);
			_documents.Should().BeEmpty("no pending row: nothing could ever seal it later");
			_inputs.Should().BeEmpty("the rendered export is never queued");
		}

		[Test]
		public async Task Adp_unprotected_departments_and_operational_subjects_keep_their_sealed_copy()
		{
			ConfigureSealing();
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(false);
			(await _service.SealPdfAsync(new SealPdfRequest { DepartmentId = Dept, SubjectKind = SignatureSubjectKinds.RecordsDocument, SubjectId = "rec-1", Pdf = RenderedPdf }))
				.Document.StorageKey.Should().NotBeNull();

			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(Dept)).ReturnsAsync(true);
			var training = await _service.SealHtmlAsync(Request());
			training.Document.StorageKey.Should().NotBeNull("a training certificate is operational: its sealed copy is kept for every department");
		}

		[Test]
		public void A_module_pdf_that_is_not_a_pdf_is_refused() =>
			FluentActions.Awaiting(() => _service.SealPdfAsync(new SealPdfRequest { DepartmentId = Dept, SubjectId = "x", Pdf = Encoding.ASCII.GetBytes("not a pdf") }))
				.Should().ThrowAsync<ArgumentException>();
	}
}
