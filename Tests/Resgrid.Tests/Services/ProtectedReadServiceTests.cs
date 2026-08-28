using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Attended protected-read pipeline (plan section 7.1): unprotected passthrough, redaction with
	/// machine-readable reasons for every grant failure mode, one batched broker round trip on a
	/// valid grant, and fail-closed behavior on broker faults — a client never sees ciphertext.
	/// </summary>
	[TestFixture]
	public class ProtectedReadServiceTests
	{
		private const int DeptId = 42;
		private const long Epoch = 3;
		private const string UserId = "user-1";

		private X509Certificate2 _certificate;
		private ProtectedDataGrantService _grantService;
		private Mock<IDepartmentDataProtectionService> _dataProtectionService;
		private Mock<IProtectedDataBrokerClient> _brokerClient;
		private ProtectedReadService _service;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			var request = new CertificateRequest("CN=adp-read-tests", ecdsa, HashAlgorithmName.SHA256);
			_certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown() => _certificate?.Dispose();

		[SetUp]
		public void SetUp()
		{
			_grantService = new ProtectedDataGrantService(() => _certificate, () => _certificate);

			_dataProtectionService = new Mock<IDepartmentDataProtectionService>();
			_dataProtectionService.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			_dataProtectionService.Setup(x => x.GetPolicyByDepartmentIdAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch, CatalogVersion = 1 });

			_brokerClient = new Mock<IProtectedDataBrokerClient>();

			_service = new ProtectedReadService(_dataProtectionService.Object, _grantService, _brokerClient.Object);
		}

		private string IssueGrant(string userId = UserId, long epoch = Epoch)
		{
			return _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = userId,
				DepartmentId = DeptId,
				PolicyEpoch = epoch,
				WindowMinutes = 15,
				Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaAtUtc = DateTime.UtcNow
			}).Token;
		}

		private static Call EnvelopedCall(int callId = 17) => new Call
		{
			CallId = callId,
			DepartmentId = DeptId,
			Number = "C-100",
			Name = "rgdp:1:1:name==",
			NatureOfCall = "rgdp:1:1:nature==",
			Address = "rgdp:1:1:address==",
			Notes = null
		};

		[Test]
		public async Task Unprotected_department_passes_through_untouched()
		{
			_dataProtectionService.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(false);
			var call = new Call { CallId = 1, Name = "Structure Fire", NatureOfCall = "Smoke showing" };

			var result = await _service.ResolveForReadAsync(DeptId, call, null, UserId);

			result.IsProtected.Should().BeFalse();
			result.ProtectedReason.Should().BeNull();
			result.RedactedFields.Should().BeEmpty();
			result.Call.Name.Should().Be("Structure Fire");
			_brokerClient.Verify(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Missing_grant_redacts_every_enveloped_field_with_step_up_required()
		{
			var call = EnvelopedCall();

			var result = await _service.ResolveForReadAsync(DeptId, call, null, UserId);

			result.IsProtected.Should().BeTrue();
			result.ProtectedReason.Should().Be("step_up_required");
			result.RedactedFields.Should().BeEquivalentTo("calls.name", "calls.natureofcall", "calls.address");
			result.Call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.Call.NatureOfCall.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.Call.Address.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.Call.Number.Should().Be("C-100", "non-cataloged fields are untouched");
			_brokerClient.Verify(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Revoked_grant_after_epoch_bump_redacts_with_grant_revoked()
		{
			var staleGrant = IssueGrant(epoch: Epoch - 1);

			var result = await _service.ResolveForReadAsync(DeptId, EnvelopedCall(), staleGrant, UserId);

			result.ProtectedReason.Should().Be("grant_revoked");
			result.Call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task Grant_bound_to_another_user_redacts_with_protected_access_denied()
		{
			var foreignGrant = IssueGrant(userId: "someone-else");

			var result = await _service.ResolveForReadAsync(DeptId, EnvelopedCall(), foreignGrant, UserId);

			result.ProtectedReason.Should().Be("protected_access_denied");
			result.Call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task Valid_grant_batches_one_broker_request_and_substitutes_plaintext()
		{
			var calls = new List<Call> { EnvelopedCall(17), EnvelopedCall(18) };
			IReadOnlyList<ProtectedFieldOperationItem> sentItems = null;
			_brokerClient.Setup(x => x.DecryptAsync(DeptId, It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, string, IReadOnlyList<ProtectedFieldOperationItem>, CancellationToken>(
					(d, g, r, items, ct) => sentItems = items)
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult
						{
							FieldId = i.FieldId,
							RowKey = i.RowKey,
							Value = $"plain:{i.RowKey}:{i.FieldId}"
						}).ToList()
					});

			var results = await _service.ResolveForReadAsync(DeptId, calls, IssueGrant(), UserId);

			sentItems.Should().HaveCount(6, "three enveloped fields per call, one batch");
			results.Should().OnlyContain(r => r.ProtectedReason == null && r.RedactedFields.Count == 0 && r.IsProtected);
			results[0].Call.Name.Should().Be("plain:17:calls.name");
			results[1].Call.NatureOfCall.Should().Be("plain:18:calls.natureofcall");
			_brokerClient.Verify(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Broker_fault_redacts_everything_and_never_leaks_ciphertext()
		{
			_brokerClient.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedDataBrokerResult { Success = false, ErrorCode = "broker_unavailable" });

			var result = await _service.ResolveForReadAsync(DeptId, EnvelopedCall(), IssueGrant(), UserId);

			result.ProtectedReason.Should().Be("broker_unavailable");
			result.Call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.Call.NatureOfCall.Should().NotStartWith("rgdp:");
		}

		[Test]
		public async Task Per_item_broker_error_redacts_only_that_field()
		{
			_brokerClient.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => i.FieldId == "calls.address"
							? new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, ErrorCode = "decrypt_failed" }
							: new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = "plain" }).ToList()
					});

			var result = await _service.ResolveForReadAsync(DeptId, EnvelopedCall(), IssueGrant(), UserId);

			result.Call.Name.Should().Be("plain");
			result.Call.Address.Should().Be(ProtectedDataEnvelope.RedactionValue);
			result.RedactedFields.Should().BeEquivalentTo("calls.address");
			result.ProtectedReason.Should().Be("broker_unavailable");
		}

		[Test]
		public void Field_accessor_map_covers_every_catalog_v1_calls_column()
		{
			var callsBinding = AdpTableBindings.V1.Single(b => b.TableName == "Calls");
			var boundFieldIds = callsBinding.Columns.Select(c => c.FieldId).ToList();

			ProtectedReadService.CallFieldAccessors.Keys.Should().BeEquivalentTo(boundFieldIds,
				"a catalog binding without a read accessor would silently leak an envelope");
		}

		[Test]
		public void Child_accessor_maps_cover_every_catalog_v1_child_column()
		{
			var notesBinding = AdpTableBindings.V1.Single(b => b.TableName == "CallNotes");
			var noteAccessorIds = ProtectedReadService.NoteFieldAccessors.Keys
				.Concat(ProtectedReadService.NoteCompanionAccessors.Keys);
			noteAccessorIds.Should().BeEquivalentTo(notesBinding.Columns.Select(c => c.FieldId));

			var attachmentsBinding = AdpTableBindings.V1.Single(b => b.TableName == "CallAttachments");
			var attachmentAccessorIds = ProtectedReadService.AttachmentFieldAccessors.Keys
				.Concat(ProtectedReadService.AttachmentCompanionAccessors.Keys)
				.Concat(new[] { ProtectedReadService.AttachmentDataFieldId });
			attachmentAccessorIds.Should().BeEquivalentTo(attachmentsBinding.Columns.Select(c => c.FieldId));

			var contactsBinding = AdpTableBindings.V1.Single(b => b.TableName == "Contacts");
			var contactAccessorIds = ProtectedReadService.ContactFieldAccessors.Keys
				.Concat(new[] { ProtectedReadService.ContactImageFieldId });
			contactAccessorIds.Should().BeEquivalentTo(contactsBinding.Columns.Select(c => c.FieldId));

			var contactNotesBinding = AdpTableBindings.V1.Single(b => b.TableName == "ContactNotes");
			ProtectedReadService.ContactNoteFieldAccessors.Keys
				.Should().BeEquivalentTo(contactNotesBinding.Columns.Select(c => c.FieldId));
		}

		[Test]
		public async Task Contacts_redact_without_a_grant_and_strip_the_enveloped_image()
		{
			var contact = new Contact
			{
				ContactId = "c-1",
				DepartmentId = DeptId,
				FirstName = "rgdp:1:1:first==",
				CellPhoneNumber = "rgdp:1:1:cell==",
				Website = "https://example.org",
				Image = System.Text.Encoding.ASCII.GetBytes("rgdpb:1:1:").Concat(new byte[] { 1, 2 }).ToArray()
			};

			var result = await _service.ResolveContactsForReadAsync(DeptId, new[] { contact }, null, UserId);

			result.IsProtected.Should().BeTrue();
			result.ProtectedReason.Should().Be("step_up_required");
			result.RedactedFields.Should().BeEquivalentTo("contacts.firstname", "contacts.cellphonenumber");
			contact.FirstName.Should().Be(ProtectedDataEnvelope.RedactionValue);
			contact.CellPhoneNumber.Should().Be(ProtectedDataEnvelope.RedactionValue);
			contact.Website.Should().Be("https://example.org", "non-cataloged fields are untouched");
			contact.Image.Should().BeNull("enveloped image bytes must never ride out through a serializer");
		}

		[Test]
		public async Task Contacts_reveal_with_a_valid_grant_in_one_broker_batch()
		{
			var contact = new Contact { ContactId = "c-1", DepartmentId = DeptId, FirstName = "rgdp:1:1:first==", LastName = "rgdp:1:1:last==" };
			_brokerClient.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult
						{
							FieldId = i.FieldId,
							RowKey = i.RowKey,
							Value = i.FieldId == "contacts.firstname" ? "Jane" : "Smith"
						}).ToList()
					});

			var result = await _service.ResolveContactsForReadAsync(DeptId, new[] { contact }, IssueGrant(), UserId);

			result.ProtectedReason.Should().BeNull();
			contact.FirstName.Should().Be("Jane");
			contact.LastName.Should().Be("Smith");
			_brokerClient.Verify(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ── protected writes ─────────────────────────────────────────────────────

		private void SetupWriteEnforced(bool enforced = true)
		{
			_dataProtectionService.Setup(x => x.ShouldEncryptNewWritesAsync(DeptId)).ReturnsAsync(enforced);
		}

		private void SetupEncryptEcho()
		{
			_brokerClient.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult
						{
							FieldId = i.FieldId,
							RowKey = i.RowKey,
							Value = i.IsBinary
								? Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("rgdpb:1:1:").Concat(new byte[] { 5 }).ToArray())
								: $"rgdp:1:1:{i.FieldId}=="
						}).ToList()
					});
		}

		[Test]
		public async Task Write_passes_through_when_the_department_is_not_in_an_encrypt_state()
		{
			SetupWriteEnforced(false);
			var call = new Call { CallId = 17, DepartmentId = DeptId, Name = "Structure Fire" };

			var result = await _service.PrepareCallWriteAsync(DeptId, call, null, null, UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			result.IsProtected.Should().BeFalse();
			call.Name.Should().Be("Structure Fire");
		}

		[Test]
		public async Task Attended_write_without_a_grant_is_blocked_before_any_broker_call()
		{
			SetupWriteEnforced();
			var call = new Call { CallId = 17, DepartmentId = DeptId, Name = "Structure Fire" };

			var result = await _service.PrepareCallWriteAsync(DeptId, call, null, null, UserId, workloadCaller: false);

			result.Success.Should().BeFalse();
			result.Reason.Should().Be("step_up_required");
			call.Name.Should().Be("Structure Fire", "a blocked write must not half-mutate the entity");
			_brokerClient.Verify(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Attended_write_with_a_valid_grant_envelopes_every_cataloged_field_in_one_batch()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var call = new Call { CallId = 17, DepartmentId = DeptId, Number = "C-100", Name = "Structure Fire", NatureOfCall = "Smoke showing" };

			var result = await _service.PrepareCallWriteAsync(DeptId, call, null, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			result.IsProtected.Should().BeTrue();
			call.Name.Should().Be("rgdp:1:1:calls.name==");
			call.NatureOfCall.Should().Be("rgdp:1:1:calls.natureofcall==");
			call.Number.Should().Be("C-100", "non-cataloged fields are untouched");
			_brokerClient.Verify(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Workload_write_encrypts_without_a_grant_through_the_workload_lane()
		{
			SetupWriteEnforced();
			string sentGrant = "unset";
			_brokerClient.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, string, IReadOnlyList<ProtectedFieldOperationItem>, CancellationToken>(
					(d, g, r, items, ct) => sentGrant = g)
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = "rgdp:1:1:x==" }).ToList()
					});
			var call = new Call { CallId = 17, DepartmentId = DeptId, Name = "Text-to-call import" };

			var result = await _service.PrepareCallWriteAsync(DeptId, call, null, null, UserId, workloadCaller: true);

			result.Success.Should().BeTrue();
			sentGrant.Should().BeNull("the workload lane sends no grant");
			call.Name.Should().StartWith("rgdp:");
		}

		[Test]
		public async Task Redacted_sentinel_on_an_edit_restores_the_stored_envelope()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var stored = new Call { CallId = 17, Name = "rgdp:1:1:storedname==", NatureOfCall = "rgdp:1:1:storednature==" };
			var edited = new Call
			{
				CallId = 17,
				DepartmentId = DeptId,
				Name = ProtectedDataEnvelope.RedactionValue,
				NatureOfCall = "Updated nature"
			};

			var result = await _service.PrepareCallWriteAsync(DeptId, edited, stored, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			edited.Name.Should().Be("rgdp:1:1:storedname==", "REDACTED means unchanged — the stored envelope survives");
			edited.NatureOfCall.Should().Be("rgdp:1:1:calls.natureofcall==", "genuinely changed fields encrypt");
		}

		[Test]
		public async Task Redacted_sentinel_without_a_stored_row_is_never_encrypted()
		{
			SetupWriteEnforced();
			IReadOnlyList<ProtectedFieldOperationItem> sentItems = null;
			_brokerClient.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, string, IReadOnlyList<ProtectedFieldOperationItem>, CancellationToken>(
					(d, g, r, items, ct) => sentItems = items)
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = $"rgdp:1:1:{i.FieldId}==" }).ToList()
					});
			var call = new Call
			{
				CallId = 17,
				DepartmentId = DeptId,
				Name = ProtectedDataEnvelope.RedactionValue,
				NatureOfCall = "Smoke showing"
			};

			var result = await _service.PrepareCallWriteAsync(DeptId, call, null, null, UserId, workloadCaller: true);

			result.Success.Should().BeTrue();
			sentItems.Select(i => i.FieldId).Should().BeEquivalentTo(new[] { "calls.natureofcall" },
				"the placeholder must never be enveloped — that would destroy the original");
			call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task Broker_fault_blocks_the_write_and_applies_nothing()
		{
			SetupWriteEnforced();
			_brokerClient.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedDataBrokerResult { Success = false, ErrorCode = "broker_unavailable" });
			var call = new Call { CallId = 17, DepartmentId = DeptId, Name = "Structure Fire" };

			var result = await _service.PrepareCallWriteAsync(DeptId, call, null, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeFalse();
			result.Reason.Should().Be("broker_unavailable");
			call.Name.Should().Be("Structure Fire", "all-or-nothing: nothing applies on failure");
		}

		[Test]
		public async Task Note_write_moves_coordinates_into_companion_envelopes_and_marks_the_row()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var note = new CallNote { CallNoteId = 7, CallId = 17, Note = "Occupant on O2", Latitude = 39.19m, Longitude = -119.76m };

			var result = await _service.PrepareCallNoteWriteAsync(DeptId, note, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			note.IsProtected.Should().BeTrue();
			note.Note.Should().Be("rgdp:1:1:callnotes.note==");
			note.Latitude.Should().BeNull("the typed column nulls; the envelope companion carries the value");
			note.ProtectedLatitudeEnvelope.Should().Be("rgdp:1:1:callnotes.latitude==");
			note.Longitude.Should().BeNull();
			note.ProtectedLongitudeEnvelope.Should().Be("rgdp:1:1:callnotes.longitude==");
		}

		[Test]
		public async Task Attachment_write_encrypts_the_binary_payload_too()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var attachment = new CallAttachment
			{
				CallAttachmentId = 9,
				CallId = 17,
				FileName = "photo.png",
				Data = new byte[] { 1, 2, 3 }
			};

			var result = await _service.PrepareCallAttachmentWriteAsync(DeptId, attachment, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			attachment.IsProtected.Should().BeTrue();
			attachment.FileName.Should().StartWith("rgdp:");
			System.Text.Encoding.ASCII.GetString(attachment.Data, 0, 6).Should().Be("rgdpb:");
		}

		[Test]
		public async Task Write_preflight_blocks_attended_callers_without_a_grant_and_passes_workload_callers()
		{
			SetupWriteEnforced();

			var attended = await _service.PreflightWriteAsync(DeptId, null, UserId, workloadCaller: false);
			attended.Success.Should().BeFalse();
			attended.Reason.Should().Be("step_up_required");

			var workload = await _service.PreflightWriteAsync(DeptId, null, UserId, workloadCaller: true);
			workload.Success.Should().BeTrue();
			workload.IsProtected.Should().BeTrue();
		}

		[Test]
		public async Task Contact_notes_redact_without_a_grant()
		{
			var note = new ContactNote { ContactNoteId = "cn-1", ContactId = "c-1", Note = "rgdp:1:1:note==" };

			var result = await _service.ResolveContactNotesForReadAsync(DeptId, new[] { note }, null, UserId);

			result.ProtectedReason.Should().Be("step_up_required");
			result.RedactedFields.Should().BeEquivalentTo("contactnotes.note");
			note.Note.Should().Be(ProtectedDataEnvelope.RedactionValue);
		}

		private static CallNote EnvelopedNote(int noteId = 7) => new CallNote
		{
			CallNoteId = noteId,
			CallId = 17,
			Note = "rgdp:1:1:note==",
			IsProtected = true,
			ProtectedLatitudeEnvelope = "rgdp:1:1:lat==",
			ProtectedLongitudeEnvelope = "rgdp:1:1:lon=="
		};

		[Test]
		public async Task Notes_redact_text_and_leave_companion_coordinates_null_without_a_grant()
		{
			var note = EnvelopedNote();

			var result = await _service.ResolveNotesForReadAsync(DeptId, new[] { note }, null, UserId);

			result.IsProtected.Should().BeTrue();
			result.ProtectedReason.Should().Be("step_up_required");
			result.RedactedFields.Should().BeEquivalentTo("callnotes.note", "callnotes.latitude", "callnotes.longitude");
			note.Note.Should().Be(ProtectedDataEnvelope.RedactionValue);
			note.Latitude.Should().BeNull();
			note.Longitude.Should().BeNull();
		}

		[Test]
		public async Task Notes_reveal_text_and_parse_companion_coordinates_with_a_valid_grant()
		{
			var note = EnvelopedNote();
			_brokerClient.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult
						{
							FieldId = i.FieldId,
							RowKey = i.RowKey,
							Value = i.FieldId switch
							{
								"callnotes.note" => "Occupant on O2",
								"callnotes.latitude" => "39.1911420",
								_ => "-119.7674100"
							}
						}).ToList()
					});

			var result = await _service.ResolveNotesForReadAsync(DeptId, new[] { note }, IssueGrant(), UserId);

			result.ProtectedReason.Should().BeNull();
			note.Note.Should().Be("Occupant on O2");
			note.Latitude.Should().Be(39.1911420m);
			note.Longitude.Should().Be(-119.7674100m);
		}

		private static byte[] BinaryEnvelope() =>
			System.Text.Encoding.ASCII.GetBytes("rgdpb:1:1:").Concat(new byte[] { 1, 2, 3, 4 }).ToArray();

		[Test]
		public async Task Attachment_data_is_stripped_on_metadata_only_reads_and_decrypted_when_opted_in()
		{
			var metadataOnly = new CallAttachment { CallAttachmentId = 9, CallId = 17, FileName = "rgdp:1:1:fn==", Data = BinaryEnvelope(), IsProtected = true };
			await _service.ResolveAttachmentsForReadAsync(DeptId, new[] { metadataOnly }, null, UserId, includeData: false);
			metadataOnly.Data.Should().BeNull("ciphertext bytes must never ride out on a metadata read");
			metadataOnly.FileName.Should().Be(ProtectedDataEnvelope.RedactionValue);

			var plaintextBytes = new byte[] { 9, 9, 9 };
			_brokerClient.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult
						{
							FieldId = i.FieldId,
							RowKey = i.RowKey,
							Value = i.FieldId == ProtectedReadService.AttachmentDataFieldId
								? Convert.ToBase64String(plaintextBytes)
								: "photo.png"
						}).ToList()
					});

			var withData = new CallAttachment { CallAttachmentId = 10, CallId = 17, FileName = "rgdp:1:1:fn==", Data = BinaryEnvelope(), IsProtected = true };
			var result = await _service.ResolveAttachmentsForReadAsync(DeptId, new[] { withData }, IssueGrant(), UserId, includeData: true);

			result.ProtectedReason.Should().BeNull();
			withData.FileName.Should().Be("photo.png");
			withData.Data.Should().BeEquivalentTo(plaintextBytes);
		}

		[Test]
		public async Task Attachment_data_redacts_to_null_when_the_grant_is_missing_on_a_data_read()
		{
			var attachment = new CallAttachment { CallAttachmentId = 11, CallId = 17, FileName = "photo.png", Data = BinaryEnvelope(), IsProtected = true };

			var result = await _service.ResolveAttachmentsForReadAsync(DeptId, new[] { attachment }, null, UserId, includeData: true);

			result.ProtectedReason.Should().Be("step_up_required");
			result.RedactedFields.Should().Contain(ProtectedReadService.AttachmentDataFieldId);
			attachment.Data.Should().BeNull();
		}

		[Test]
		public async Task Call_resolution_carries_populated_children_in_the_same_batch()
		{
			var call = EnvelopedCall();
			call.CallNotes = new List<CallNote> { EnvelopedNote() };
			IReadOnlyList<ProtectedFieldOperationItem> sentItems = null;
			_brokerClient.Setup(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, string, IReadOnlyList<ProtectedFieldOperationItem>, CancellationToken>(
					(d, g, r, items, ct) => sentItems = items)
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = "1" }).ToList()
					});

			await _service.ResolveForReadAsync(DeptId, call, IssueGrant(), UserId);

			sentItems.Should().NotBeNull();
			sentItems.Select(i => i.FieldId).Should().Contain(new[] { "calls.name", "callnotes.note", "callnotes.latitude" });
			_brokerClient.Verify(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ── contact writes ───────────────────────────────────────────────────────

		[Test]
		public async Task Contact_write_envelopes_text_fields_and_the_binary_image()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var contact = new Contact
			{
				ContactId = "contact-guid-1",
				DepartmentId = DeptId,
				FirstName = "Pat",
				LastName = "Doe",
				CellPhoneNumber = "555-0100",
				Image = new byte[] { 1, 2, 3 }
			};

			var result = await _service.PrepareContactWriteAsync(DeptId, contact, null, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			result.Changed.Should().BeTrue();
			contact.FirstName.Should().Be("rgdp:1:1:contacts.firstname==");
			contact.LastName.Should().Be("rgdp:1:1:contacts.lastname==");
			contact.CellPhoneNumber.Should().Be("rgdp:1:1:contacts.cellphonenumber==");
			ProtectedReadService.IsBinaryEnveloped(contact.Image).Should().BeTrue();
		}

		[Test]
		public async Task Contact_write_skips_enveloped_values_and_the_workload_lane_sends_no_grant()
		{
			SetupWriteEnforced();
			string sentGrant = "unset";
			IReadOnlyList<ProtectedFieldOperationItem> sentItems = null;
			_brokerClient.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, string, IReadOnlyList<ProtectedFieldOperationItem>, CancellationToken>(
					(d, g, r, items, ct) => { sentGrant = g; sentItems = items; })
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = $"rgdp:1:1:{i.FieldId}==" }).ToList()
					});
			var contact = new Contact
			{
				ContactId = "contact-guid-1",
				DepartmentId = DeptId,
				FirstName = "rgdp:1:1:already==",
				LastName = "Doe"
			};

			var result = await _service.PrepareContactWriteAsync(DeptId, contact, null, null, UserId, workloadCaller: true);

			result.Success.Should().BeTrue();
			sentGrant.Should().BeNull("the workload lane sends no grant");
			sentItems.Select(i => i.FieldId).Should().BeEquivalentTo(new[] { "contacts.lastname" });
			contact.FirstName.Should().Be("rgdp:1:1:already==", "already-enveloped values are never re-encrypted");
		}

		[Test]
		public async Task Contact_redacted_sentinel_on_an_edit_restores_the_stored_envelope()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var stored = new Contact { ContactId = "contact-guid-1", FirstName = "rgdp:1:1:storedfirst==" };
			var edited = new Contact
			{
				ContactId = "contact-guid-1",
				DepartmentId = DeptId,
				FirstName = ProtectedDataEnvelope.RedactionValue,
				LastName = "Updated"
			};

			var result = await _service.PrepareContactWriteAsync(DeptId, edited, stored, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			edited.FirstName.Should().Be("rgdp:1:1:storedfirst==");
			edited.LastName.Should().Be("rgdp:1:1:contacts.lastname==");
		}

		[Test]
		public async Task Contact_note_write_envelopes_the_note_and_a_broker_fault_applies_nothing()
		{
			SetupWriteEnforced();
			SetupEncryptEcho();
			var note = new ContactNote { ContactNoteId = "note-guid-1", ContactId = "contact-guid-1", DepartmentId = DeptId, Note = "Gate code 4411" };

			var result = await _service.PrepareContactNoteWriteAsync(DeptId, note, IssueGrant(), UserId, workloadCaller: false);

			result.Success.Should().BeTrue();
			note.Note.Should().Be("rgdp:1:1:contactnotes.note==");

			_brokerClient.Setup(x => x.EncryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedDataBrokerResult { Success = false, ErrorCode = "broker_unavailable" });
			var faulted = new ContactNote { ContactNoteId = "note-guid-2", ContactId = "contact-guid-1", DepartmentId = DeptId, Note = "Second note" };

			var blocked = await _service.PrepareContactNoteWriteAsync(DeptId, faulted, IssueGrant(), UserId, workloadCaller: false);

			blocked.Success.Should().BeFalse();
			blocked.Reason.Should().Be("broker_unavailable");
			faulted.Note.Should().Be("Second note", "all-or-nothing: nothing applies on failure");
		}
	}
}
