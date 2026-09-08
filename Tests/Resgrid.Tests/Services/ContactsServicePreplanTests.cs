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
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Contacts plan Phase A: pre-plans, hazards, site attachments and call surfacing on ContactsService,
	/// plus the signed contact-file link validator. Repositories are mocked; the service composes. The
	/// ADP write net (catalog v12) is stubbed to an allow and its calls are asserted.
	/// </summary>
	[TestFixture]
	public class ContactsServicePreplanTests
	{
		private const int DepartmentId = 7;
		private const string ContactId = "contact-1";
		private const string OtherContactId = "contact-2";

		private Mock<IContactsRepository> _contactsRepo;
		private Mock<IContactNotesRepository> _notesRepo;
		private Mock<IContactCategoryRepository> _categoryRepo;
		private Mock<IContactNoteTypesRepository> _noteTypesRepo;
		private Mock<IContactAssociationsRepository> _associationsRepo;
		private Mock<IContactPreplanRepository> _preplanRepo;
		private Mock<IContactPreplanHazardRepository> _hazardRepo;
		private Mock<IContactAttachmentRepository> _attachmentRepo;
		private Mock<ICallsRepository> _callsRepo;
		private Mock<ICallContactsRepository> _callContactsRepo;
		private Mock<IEventAggregator> _events;
		private Mock<IProtectedWriteService> _protectedWrites;
		private Mock<IContactPreplanOwnershipGate> _ownershipGate;
		private List<AuditEvent> _audits;
		private ContactsService _service;

		[SetUp]
		public void SetUp()
		{
			_contactsRepo = new Mock<IContactsRepository>();
			_notesRepo = new Mock<IContactNotesRepository>();
			_categoryRepo = new Mock<IContactCategoryRepository>();
			_noteTypesRepo = new Mock<IContactNoteTypesRepository>();
			_associationsRepo = new Mock<IContactAssociationsRepository>();
			_preplanRepo = new Mock<IContactPreplanRepository>();
			_hazardRepo = new Mock<IContactPreplanHazardRepository>();
			_attachmentRepo = new Mock<IContactAttachmentRepository>();
			_callsRepo = new Mock<ICallsRepository>();
			_callContactsRepo = new Mock<ICallContactsRepository>();
			_events = new Mock<IEventAggregator>();
			_protectedWrites = new Mock<IProtectedWriteService>();
			_audits = new List<AuditEvent>();

			_events.Setup(x => x.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));

			// The ADP write net runs on every save (catalog v12). Stubbed to a plain allow here — a loose mock
			// returns a null Task and NREs at the await.
			_protectedWrites.Setup(x => x.PrepareContactPreplanWriteAsync(It.IsAny<int>(), It.IsAny<ContactPreplan>(), It.IsAny<ContactPreplan>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_protectedWrites.Setup(x => x.PrepareContactPreplanHazardWriteAsync(It.IsAny<int>(), It.IsAny<ContactPreplanHazard>(), It.IsAny<ContactPreplanHazard>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_protectedWrites.Setup(x => x.PrepareContactAttachmentWriteAsync(It.IsAny<int>(), It.IsAny<ContactAttachment>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_contactsRepo.Setup(x => x.GetByIdAsync(ContactId)).ReturnsAsync(new Contact { ContactId = ContactId, DepartmentId = DepartmentId, ContactType = 1, CompanyName = "Main Street Mill" });
			_contactsRepo.Setup(x => x.GetByIdAsync(OtherContactId)).ReturnsAsync(new Contact { ContactId = OtherContactId, DepartmentId = DepartmentId, ContactType = 0, FirstName = "Ada", LastName = "Lovelace" });

			_preplanRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ContactPreplan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync<ContactPreplan, CancellationToken, bool, IContactPreplanRepository, ContactPreplan>((p, _, __) =>
				{
					if (string.IsNullOrWhiteSpace(p.ContactPreplanId))
						p.ContactPreplanId = Guid.NewGuid().ToString();
					return p;
				});
			_hazardRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ContactPreplanHazard>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync<ContactPreplanHazard, CancellationToken, bool, IContactPreplanHazardRepository, ContactPreplanHazard>((h, _, __) =>
				{
					if (string.IsNullOrWhiteSpace(h.ContactPreplanHazardId))
						h.ContactPreplanHazardId = Guid.NewGuid().ToString();
					return h;
				});
			_attachmentRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ContactAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync<ContactAttachment, CancellationToken, bool, IContactAttachmentRepository, ContactAttachment>((a, _, __) =>
				{
					if (a.ContactAttachmentId == 0)
						a.ContactAttachmentId = 42;
					return a;
				});
			_hazardRepo.Setup(x => x.GetHazardsByPreplanIdAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<ContactPreplanHazard>());

			_ownershipGate = new Mock<IContactPreplanOwnershipGate>();
			_ownershipGate.Setup(x => x.IsRecordsOwnedAsync(It.IsAny<int>())).ReturnsAsync(false);
			_ownershipGate.Setup(x => x.GetPreplanProjectionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, ContactPreplan>());

			_service = new ContactsService(_contactsRepo.Object, _notesRepo.Object, _categoryRepo.Object, _noteTypesRepo.Object,
				_associationsRepo.Object, _preplanRepo.Object, _hazardRepo.Object, _attachmentRepo.Object, _callsRepo.Object,
				_callContactsRepo.Object, _events.Object,
				new Lazy<IProtectedWriteService>(() => _protectedWrites.Object),
				new Lazy<IContactPreplanOwnershipGate>(() => _ownershipGate.Object));
		}

		#region RMS-5 structure ownership gate

		[Test]
		public async Task Preplan_writes_are_refused_once_records_owns_structure_data()
		{
			_ownershipGate.Setup(x => x.IsRecordsOwnedAsync(DepartmentId)).ReturnsAsync(true);
			Func<Task> save = () => _service.SavePreplanAsync(new ContactPreplan { DepartmentId = DepartmentId, ContactId = ContactId }, "user-1", null, null);
			(await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage(IContactPreplanOwnershipGate.RecordsOwnedReason);
			Func<Task> delete = () => _service.DeletePreplanAsync(ContactId, DepartmentId, "user-1", null, null);
			await delete.Should().ThrowAsync<InvalidOperationException>();
			_preplanRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<ContactPreplan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Preplan_reads_project_from_records_once_it_owns_structure_data()
		{
			_ownershipGate.Setup(x => x.IsRecordsOwnedAsync(DepartmentId)).ReturnsAsync(true);
			var projected = new ContactPreplan { ContactPreplanId = "occ:1", DepartmentId = DepartmentId, ContactId = ContactId, GateCode = "4471", TacticalSummary = "Sprinklered", Hazards = new List<ContactPreplanHazard> { new ContactPreplanHazard { ContactPreplanHazardId = "occ:h1", ContactId = ContactId, Title = "Propane", Severity = 3 } } };
			_ownershipGate.Setup(x => x.GetPreplanProjectionsAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, ContactPreplan> { [ContactId] = projected });

			var preplan = await _service.GetPreplanByContactIdAsync(ContactId, DepartmentId);
			preplan.Should().BeSameAs(projected);
			(await _service.GetHazardsByContactIdAsync(ContactId, DepartmentId)).Should().ContainSingle(h => h.Title == "Propane");
			_preplanRepo.Verify(x => x.GetPreplanByContactIdAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never, "Contacts must not read its own pre-plan rows once Records owns the master");
		}

		#endregion

		#region Alert notes

		[Test]
		public void IsLiveAlertNote_filters_expired_deleted_non_alert_and_foreign_notes()
		{
			var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
			ContactNote Note(Action<ContactNote> mutate)
			{
				var note = new ContactNote { DepartmentId = DepartmentId, ShouldAlert = true, IsDeleted = false, ExpiresOn = null };
				mutate(note);
				return note;
			}

			ContactsService.IsLiveAlertNote(Note(n => { }), DepartmentId, now).Should().BeTrue("a ShouldAlert note with no expiry never expires");
			ContactsService.IsLiveAlertNote(Note(n => n.ExpiresOn = now.AddDays(1)), DepartmentId, now).Should().BeTrue();
			ContactsService.IsLiveAlertNote(Note(n => n.ExpiresOn = now.AddMinutes(-1)), DepartmentId, now).Should().BeFalse("expired");
			ContactsService.IsLiveAlertNote(Note(n => n.IsDeleted = true), DepartmentId, now).Should().BeFalse("deleted");
			ContactsService.IsLiveAlertNote(Note(n => n.ShouldAlert = false), DepartmentId, now).Should().BeFalse("not an alert");
			ContactsService.IsLiveAlertNote(Note(n => n.DepartmentId = DepartmentId + 1), DepartmentId, now).Should().BeFalse("another department");
			ContactsService.IsLiveAlertNote(null, DepartmentId, now).Should().BeFalse();
		}

		[Test]
		public async Task GetAlertNotesByContactIds_returns_only_live_alert_notes_per_contact()
		{
			_notesRepo.Setup(x => x.GetContactNotesByContactIdAsync(ContactId)).ReturnsAsync(new List<ContactNote>
			{
				new ContactNote { ContactNoteId = "live", DepartmentId = DepartmentId, ShouldAlert = true },
				new ContactNote { ContactNoteId = "expired", DepartmentId = DepartmentId, ShouldAlert = true, ExpiresOn = DateTime.UtcNow.AddDays(-1) },
				new ContactNote { ContactNoteId = "plain", DepartmentId = DepartmentId, ShouldAlert = false }
			});
			_notesRepo.Setup(x => x.GetContactNotesByContactIdAsync(OtherContactId)).ReturnsAsync(new List<ContactNote>());

			var result = await _service.GetAlertNotesByContactIdsAsync(DepartmentId, new[] { ContactId, OtherContactId, ContactId, null });

			result.Keys.Should().BeEquivalentTo(new[] { ContactId, OtherContactId });
			result[ContactId].Select(x => x.ContactNoteId).Should().Equal("live");
			result[OtherContactId].Should().BeEmpty();
		}

		#endregion

		#region Pre-plans

		[Test]
		public async Task SavePreplan_creates_a_new_plan_runs_the_protected_write_net_and_audits_without_the_gate_code()
		{
			_preplanRepo.Setup(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync((ContactPreplan)null);
			ContactPreplan persisted = null;
			_preplanRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ContactPreplan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<ContactPreplan, CancellationToken, bool>((p, _, __) => persisted = p.CloneJson())
				.ReturnsAsync<ContactPreplan, CancellationToken, bool, IContactPreplanRepository, ContactPreplan>((p, _, __) => { p.ContactPreplanId = "plan-1"; return p; });

			var saved = await _service.SavePreplanAsync(new ContactPreplan
			{
				ContactId = ContactId,
				DepartmentId = DepartmentId,
				GateCode = "4321#",
				KnoxBoxLocation = "North door",
				NextReviewDue = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
			}, "user-1", "10.0.0.1", "test-agent");

			persisted.AddedByUserId.Should().Be("user-1");
			persisted.IsDeleted.Should().BeFalse();
			saved.ContactPreplanId.Should().Be("plan-1");
			saved.GateCode.Should().Be("4321#", "an unprotected department stores and returns the plaintext");
			saved.Hazards.Should().NotBeNull();

			// The ADP net (catalog v12) runs after the insert with the assigned id and no stored row to restore from.
			_protectedWrites.Verify(x => x.PrepareContactPreplanWriteAsync(DepartmentId, It.Is<ContactPreplan>(p => p.ContactPreplanId == "plan-1"),
				null, null, null, true, It.IsAny<CancellationToken>()), Times.Once);

			_audits.Should().ContainSingle();
			_audits[0].Type.Should().Be(AuditLogTypes.ContactPreplanAdded);
			_audits[0].UserId.Should().Be("user-1");
			_audits[0].IpAddress.Should().Be("10.0.0.1");
			_audits[0].After.Should().NotContain("4321#", "audit snapshots never carry the gate code");
			_audits[0].After.Should().Contain(ContactsService.GateCodeAuditMask);
		}

		[Test]
		public async Task SavePreplan_re_persists_the_enveloped_row_when_the_protected_net_changed_it()
		{
			_preplanRepo.Setup(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync((ContactPreplan)null);
			_protectedWrites.Setup(x => x.PrepareContactPreplanWriteAsync(It.IsAny<int>(), It.IsAny<ContactPreplan>(), It.IsAny<ContactPreplan>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.Callback<int, ContactPreplan, ContactPreplan, string, string, bool, CancellationToken>((_, p, __, ___, ____, _____, ______) =>
				{
					p.GateCode = "rgdp:1:1:contactpreplans.gatecode==";
					p.IsProtected = true;
				})
				.ReturnsAsync(new ProtectedWriteResult { Success = true, Changed = true, IsProtected = true });

			var saved = await _service.SavePreplanAsync(new ContactPreplan { ContactId = ContactId, DepartmentId = DepartmentId, GateCode = "4321#" }, "user-1", null, null);

			saved.GateCode.Should().Be("rgdp:1:1:contactpreplans.gatecode==");
			_preplanRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<ContactPreplan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2),
				"the enveloped row is written back after the broker sealed it");
			_audits[0].After.Should().NotContain("4321#").And.NotContain("rgdp:", "neither plaintext nor ciphertext reaches the audit log");
		}

		[Test]
		public async Task SavePreplan_fails_closed_when_the_protected_write_is_blocked()
		{
			_preplanRepo.Setup(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync((ContactPreplan)null);
			_protectedWrites.Setup(x => x.PrepareContactPreplanWriteAsync(It.IsAny<int>(), It.IsAny<ContactPreplan>(), It.IsAny<ContactPreplan>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = false, Reason = "broker_unavailable" });

			Func<Task> act = () => _service.SavePreplanAsync(new ContactPreplan { ContactId = ContactId, DepartmentId = DepartmentId, GateCode = "4321#" }, "user-1", null, null);

			(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("broker_unavailable");
		}

		[Test]
		public async Task SavePreplan_updates_the_existing_row_keeps_creation_provenance_and_hands_the_stored_row_to_the_net()
		{
			var existing = new ContactPreplan
			{
				ContactPreplanId = "plan-1",
				ContactId = ContactId,
				DepartmentId = DepartmentId,
				AddedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				AddedByUserId = "creator",
				GateCode = "1111"
			};
			_preplanRepo.Setup(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync(existing);

			var saved = await _service.SavePreplanAsync(new ContactPreplan
			{
				ContactId = ContactId,
				DepartmentId = DepartmentId,
				ContactPreplanId = "something-else",
				GateCode = "2222"
			}, "editor", "10.0.0.1", "test-agent");

			saved.ContactPreplanId.Should().Be("plan-1", "one live pre-plan per contact: the save lands on the existing row");
			saved.AddedByUserId.Should().Be("creator");
			saved.AddedOn.Should().Be(existing.AddedOn);
			saved.EditedByUserId.Should().Be("editor");
			saved.GateCode.Should().Be("2222");

			// The stored row is what a REDACTED sentinel restores from (an editor without a grant round-trips the placeholder).
			_protectedWrites.Verify(x => x.PrepareContactPreplanWriteAsync(DepartmentId, It.IsAny<ContactPreplan>(), existing,
				null, null, true, It.IsAny<CancellationToken>()), Times.Once);

			_audits.Should().ContainSingle();
			_audits[0].Type.Should().Be(AuditLogTypes.ContactPreplanUpdated);
			_audits[0].Before.Should().NotContain("1111");
			_audits[0].After.Should().NotContain("2222");
		}

		[Test]
		public async Task SavePreplan_rejects_a_contact_from_another_department()
		{
			_contactsRepo.Setup(x => x.GetByIdAsync("foreign")).ReturnsAsync(new Contact { ContactId = "foreign", DepartmentId = DepartmentId + 1 });

			Func<Task> act = () => _service.SavePreplanAsync(new ContactPreplan { ContactId = "foreign", DepartmentId = DepartmentId }, "user-1", null, null);

			await act.Should().ThrowAsync<InvalidOperationException>();
			_preplanRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<ContactPreplan>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_audits.Should().BeEmpty();
		}

		[Test]
		public async Task GetPreplanByContactId_returns_the_stored_values_untouched_and_loads_live_hazards()
		{
			_preplanRepo.Setup(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync(new ContactPreplan
			{
				ContactPreplanId = "plan-1",
				ContactId = ContactId,
				DepartmentId = DepartmentId,
				GateCode = "rgdp:1:1:contactpreplans.gatecode=="
			});
			_hazardRepo.Setup(x => x.GetHazardsByPreplanIdAsync("plan-1", DepartmentId)).ReturnsAsync(new List<ContactPreplanHazard>
			{
				new ContactPreplanHazard { ContactPreplanHazardId = "h1", Title = "Propane tank" },
				new ContactPreplanHazard { ContactPreplanHazardId = "h2", Title = "Old", IsDeleted = true }
			});

			var preplan = await _service.GetPreplanByContactIdAsync(ContactId, DepartmentId);

			preplan.GateCode.Should().Be("rgdp:1:1:contactpreplans.gatecode==", "the service never decrypts: the caller runs the protected read with its grant");
			preplan.Hazards.Select(x => x.ContactPreplanHazardId).Should().Equal("h1");
		}

		[Test]
		public async Task GetPreplansDueForReview_returns_only_overdue_live_plans_without_gate_codes()
		{
			var now = DateTime.UtcNow;
			_preplanRepo.Setup(x => x.GetPreplansByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<ContactPreplan>
			{
				new ContactPreplan { ContactPreplanId = "overdue", NextReviewDue = now.AddDays(-1), GateCode = "x" },
				new ContactPreplan { ContactPreplanId = "future", NextReviewDue = now.AddDays(30) },
				new ContactPreplan { ContactPreplanId = "unscheduled" },
				new ContactPreplan { ContactPreplanId = "deleted", NextReviewDue = now.AddDays(-1), IsDeleted = true }
			});

			var due = await _service.GetPreplansDueForReviewAsync(DepartmentId);

			due.Select(x => x.ContactPreplanId).Should().Equal("overdue");
			due[0].GateCode.Should().BeNull();
		}

		#endregion

		#region Hazards

		[Test]
		public async Task SaveHazard_creates_the_preplan_shell_when_the_contact_has_none_and_runs_the_net()
		{
			_preplanRepo.SetupSequence(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId))
				.ReturnsAsync((ContactPreplan)null)
				.ReturnsAsync((ContactPreplan)null);

			var saved = await _service.SaveHazardAsync(new ContactPreplanHazard
			{
				ContactId = ContactId,
				DepartmentId = DepartmentId,
				Title = "Ammonia refrigeration",
				HazardType = (int)ContactPreplanHazardTypes.Hazmat,
				Severity = (int)ContactPreplanHazardSeverities.Danger,
				ShouldAlert = true
			}, "user-1", null, null);

			saved.ContactPreplanHazardId.Should().NotBeNullOrWhiteSpace();
			saved.ContactPreplanId.Should().NotBeNullOrWhiteSpace("the hazard hangs off the shell pre-plan");
			saved.AddedByUserId.Should().Be("user-1");

			_preplanRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<ContactPreplan>(p => p.ContactId == ContactId), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_protectedWrites.Verify(x => x.PrepareContactPreplanHazardWriteAsync(DepartmentId, It.Is<ContactPreplanHazard>(h => h.Title == "Ammonia refrigeration"),
				null, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
			_audits.Select(x => x.Type).Should().Equal(AuditLogTypes.ContactPreplanAdded, AuditLogTypes.ContactPreplanUpdated);
		}

		[Test]
		public async Task SaveHazard_refuses_to_move_a_hazard_between_contacts()
		{
			_preplanRepo.Setup(x => x.GetPreplanByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync(new ContactPreplan { ContactPreplanId = "plan-1", ContactId = ContactId, DepartmentId = DepartmentId });
			_hazardRepo.Setup(x => x.GetByIdAsync("h-other")).ReturnsAsync(new ContactPreplanHazard { ContactPreplanHazardId = "h-other", ContactId = OtherContactId, DepartmentId = DepartmentId, Title = "Dog" });

			Func<Task> act = () => _service.SaveHazardAsync(new ContactPreplanHazard { ContactPreplanHazardId = "h-other", ContactId = ContactId, DepartmentId = DepartmentId, Title = "Dog" }, "user-1", null, null);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task DeleteHazard_soft_deletes_and_ignores_other_departments()
		{
			_hazardRepo.Setup(x => x.GetByIdAsync("h1")).ReturnsAsync(new ContactPreplanHazard { ContactPreplanHazardId = "h1", ContactId = ContactId, DepartmentId = DepartmentId, Title = "Dog" });
			_hazardRepo.Setup(x => x.GetByIdAsync("h-foreign")).ReturnsAsync(new ContactPreplanHazard { ContactPreplanHazardId = "h-foreign", DepartmentId = DepartmentId + 1, Title = "Dog" });

			(await _service.DeleteHazardAsync("h-foreign", DepartmentId, "user-1", null, null)).Should().BeFalse();
			(await _service.DeleteHazardAsync("h1", DepartmentId, "user-1", null, null)).Should().BeTrue();

			_hazardRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<ContactPreplanHazard>(h => h.ContactPreplanHazardId == "h1" && h.IsDeleted && h.EditedByUserId == "user-1"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_hazardRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<ContactPreplanHazard>(h => h.ContactPreplanHazardId == "h-foreign"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		#endregion

		#region Attachments

		[Test]
		public async Task SaveContactAttachment_stamps_provenance_size_runs_the_net_and_audits_metadata_only()
		{
			var data = new byte[] { 1, 2, 3, 4, 5 };

			var saved = await _service.SaveContactAttachmentAsync(new ContactAttachment
			{
				ContactId = ContactId,
				DepartmentId = DepartmentId,
				FileName = "floorplan.pdf",
				FileType = "application/pdf",
				ContactAttachmentType = (int)ContactAttachmentTypes.FloorPlan,
				Data = data
			}, "user-1", "10.0.0.1", "test-agent");

			saved.ContactAttachmentId.Should().Be(42);
			saved.Size.Should().Be(5);
			saved.Name.Should().Be("floorplan.pdf", "the title defaults to the file name");
			saved.AddedByUserId.Should().Be("user-1");

			// The identity id is the AAD row key, so the net runs after the insert assigned it.
			_protectedWrites.Verify(x => x.PrepareContactAttachmentWriteAsync(DepartmentId, It.Is<ContactAttachment>(a => a.ContactAttachmentId == 42),
				null, null, true, It.IsAny<CancellationToken>()), Times.Once);

			_audits.Should().ContainSingle();
			_audits[0].Type.Should().Be(AuditLogTypes.ContactAttachmentAdded);
			_audits[0].After.Should().Contain("floorplan.pdf").And.NotContain("\"Data\":\"AQID", "the blob never enters the audit log");
		}

		[Test]
		public async Task SaveContactAttachment_requires_file_data()
		{
			Func<Task> act = () => _service.SaveContactAttachmentAsync(new ContactAttachment { ContactId = ContactId, DepartmentId = DepartmentId, FileName = "x.pdf" }, "user-1", null, null);

			await act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task DeleteContactAttachment_soft_deletes_own_department_only()
		{
			_attachmentRepo.Setup(x => x.GetAttachmentByIdAsync(1)).ReturnsAsync(new ContactAttachment { ContactAttachmentId = 1, ContactId = ContactId, DepartmentId = DepartmentId, FileName = "a.pdf", Data = new byte[] { 1 } });
			_attachmentRepo.Setup(x => x.GetAttachmentByIdAsync(2)).ReturnsAsync(new ContactAttachment { ContactAttachmentId = 2, ContactId = ContactId, DepartmentId = DepartmentId + 1, FileName = "b.pdf" });

			(await _service.DeleteContactAttachmentAsync(2, DepartmentId, "user-1", null, null)).Should().BeFalse();
			(await _service.DeleteContactAttachmentAsync(1, DepartmentId, "user-1", null, null)).Should().BeTrue();

			_attachmentRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<ContactAttachment>(a => a.ContactAttachmentId == 1 && a.IsDeleted), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_audits.Should().ContainSingle().Which.Type.Should().Be(AuditLogTypes.ContactAttachmentRemoved);
		}

		[Test]
		public async Task GetContactAttachments_filters_by_type_and_never_returns_deleted_rows()
		{
			_attachmentRepo.Setup(x => x.GetAttachmentMetaByContactIdAsync(ContactId, DepartmentId)).ReturnsAsync(new List<ContactAttachment>
			{
				new ContactAttachment { ContactAttachmentId = 1, ContactAttachmentType = (int)ContactAttachmentTypes.FloorPlan },
				new ContactAttachment { ContactAttachmentId = 2, ContactAttachmentType = (int)ContactAttachmentTypes.SitePhoto },
				new ContactAttachment { ContactAttachmentId = 3, ContactAttachmentType = (int)ContactAttachmentTypes.FloorPlan, IsDeleted = true }
			});

			(await _service.GetContactAttachmentsAsync(ContactId, DepartmentId)).Select(x => x.ContactAttachmentId).Should().Equal(1, 2);
			(await _service.GetContactAttachmentsAsync(ContactId, DepartmentId, ContactAttachmentTypes.FloorPlan)).Select(x => x.ContactAttachmentId).Should().Equal(1);
		}

		#endregion

		#region Call surfacing

		private void SetupCallWithBothContacts(int callId)
		{
			_callsRepo.Setup(x => x.GetByIdAsync(callId)).ReturnsAsync(new Call { CallId = callId, DepartmentId = DepartmentId });
			_callContactsRepo.Setup(x => x.GetCallContactsByCallIdAsync(callId)).ReturnsAsync(new List<CallContact>
			{
				new CallContact { CallId = callId, ContactId = OtherContactId, CallContactType = 1 },
				new CallContact { CallId = callId, ContactId = ContactId, CallContactType = 0 },
				new CallContact { CallId = callId, ContactId = ContactId, CallContactType = 1 }
			});
			_preplanRepo.Setup(x => x.GetPreplansByContactIdsAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<ContactPreplan>
			{
				new ContactPreplan { ContactPreplanId = "plan-1", ContactId = ContactId, DepartmentId = DepartmentId, GateCode = "1234", HazmatOnSite = true }
			});
			_hazardRepo.Setup(x => x.GetHazardsByContactIdsAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<ContactPreplanHazard>
			{
				new ContactPreplanHazard { ContactPreplanHazardId = "h1", ContactPreplanId = "plan-1", ContactId = ContactId, Title = "Propane", ShouldAlert = true },
				new ContactPreplanHazard { ContactPreplanHazardId = "h2", ContactPreplanId = "plan-1", ContactId = ContactId, Title = "Dog" }
			});
			_attachmentRepo.Setup(x => x.GetAttachmentMetaByContactIdsAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<ContactAttachment>
			{
				new ContactAttachment { ContactAttachmentId = 5, ContactId = ContactId, FileName = "plan.pdf" }
			});
			_notesRepo.Setup(x => x.GetContactNotesByContactIdAsync(ContactId)).ReturnsAsync(new List<ContactNote>
			{
				new ContactNote { ContactNoteId = "n1", DepartmentId = DepartmentId, ShouldAlert = true, Note = "Aggressive dog" },
				new ContactNote { ContactNoteId = "n2", DepartmentId = DepartmentId, ShouldAlert = true, ExpiresOn = DateTime.UtcNow.AddDays(-2) }
			});
			_notesRepo.Setup(x => x.GetContactNotesByContactIdAsync(OtherContactId)).ReturnsAsync(new List<ContactNote>());
		}

		[Test]
		public async Task GetCallSiteInfo_composes_contacts_preplan_hazards_live_alert_notes_and_file_metadata_primary_first()
		{
			SetupCallWithBothContacts(100);

			var info = await _service.GetCallSiteInfoAsync(100, DepartmentId);

			info.CallId.Should().Be(100);
			info.Contacts.Select(x => x.Contact.ContactId).Should().Equal(new[] { ContactId, OtherContactId }, "primary first, one entry per contact");
			info.Contacts[0].CallContactType.Should().Be(0);

			var site = info.Contacts[0];
			site.Preplan.Should().NotBeNull();
			site.Preplan.GateCode.Should().Be("1234", "stored values pass through; the controller runs the protected read");
			site.Preplan.Hazards.Select(x => x.ContactPreplanHazardId).Should().Equal("h1", "h2");
			site.Hazards.Should().HaveCount(2);
			site.AlertNotes.Select(x => x.ContactNoteId).Should().Equal(new[] { "n1" }, "expired alert notes do not surface");
			site.Attachments.Select(x => x.ContactAttachmentId).Should().Equal(5);

			info.Contacts[1].Preplan.Should().BeNull();
			info.Contacts[1].Hazards.Should().BeEmpty();
		}

		[Test]
		public async Task GetCallSiteInfo_is_null_for_a_missing_or_foreign_call_and_empty_for_a_call_without_contacts()
		{
			_callsRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync((Call)null);
			_callsRepo.Setup(x => x.GetByIdAsync(2)).ReturnsAsync(new Call { CallId = 2, DepartmentId = DepartmentId + 1 });
			_callsRepo.Setup(x => x.GetByIdAsync(3)).ReturnsAsync(new Call { CallId = 3, DepartmentId = DepartmentId });
			_callContactsRepo.Setup(x => x.GetCallContactsByCallIdAsync(3)).ReturnsAsync(new List<CallContact>());

			(await _service.GetCallSiteInfoAsync(1, DepartmentId)).Should().BeNull();
			(await _service.GetCallSiteInfoAsync(2, DepartmentId)).Should().BeNull();

			var empty = await _service.GetCallSiteInfoAsync(3, DepartmentId);
			empty.Should().NotBeNull();
			empty.Contacts.Should().BeEmpty();
		}

		[Test]
		public async Task GetCallContactSummaries_counts_preplan_hazards_and_live_alert_notes_per_call()
		{
			SetupCallWithBothContacts(100);
			var call = new Call
			{
				CallId = 100,
				DepartmentId = DepartmentId,
				Contacts = new List<CallContact>
				{
					new CallContact { ContactId = OtherContactId, CallContactType = 1 },
					new CallContact { ContactId = ContactId, CallContactType = 0 }
				}
			};
			var callWithoutContacts = new Call { CallId = 101, DepartmentId = DepartmentId, Contacts = new List<CallContact>() };

			var summaries = await _service.GetCallContactSummariesAsync(DepartmentId, new[] { call, callWithoutContacts });

			summaries.Keys.Should().BeEquivalentTo(new[] { 100, 101 });
			summaries[101].Should().BeEmpty();

			var primary = summaries[100][0];
			primary.Contact.ContactId.Should().Be(ContactId);
			primary.CallContactType.Should().Be(0);
			primary.HasPreplan.Should().BeTrue();
			primary.HazardCount.Should().Be(2);
			primary.AlertNoteCount.Should().Be(1);

			var additional = summaries[100][1];
			additional.Contact.ContactId.Should().Be(OtherContactId);
			additional.HasPreplan.Should().BeFalse();
			additional.HazardCount.Should().Be(0);
			additional.AlertNoteCount.Should().Be(0);
		}

		#endregion

		#region Signed contact-file links

		[Test]
		public void Contact_file_link_validator_accepts_only_its_own_unexpired_kind()
		{
			var future = DateTime.UtcNow.AddMinutes(10).Ticks;
			var past = DateTime.UtcNow.AddMinutes(-10).Ticks;

			ContactFilesController.TryValidateSignedContactFileQuery($"c|{DepartmentId}|42|{future}", out var departmentId, out var attachmentId).Should().BeTrue();
			departmentId.Should().Be(DepartmentId);
			attachmentId.Should().Be(42);

			ContactFilesController.TryValidateSignedContactFileQuery($"c|{DepartmentId}|42|{past}", out _, out _).Should().BeFalse("expired");
			ContactFilesController.TryValidateSignedContactFileQuery($"{DepartmentId}|42|{future}", out _, out _).Should().BeFalse("a call-file link must not resolve a contact file");
			ContactFilesController.TryValidateSignedContactFileQuery($"x|{DepartmentId}|42|{future}", out _, out _).Should().BeFalse("wrong kind");
			ContactFilesController.TryValidateSignedContactFileQuery($"c|0|42|{future}", out _, out _).Should().BeFalse();
			ContactFilesController.TryValidateSignedContactFileQuery("", out _, out _).Should().BeFalse();
			ContactFilesController.TryValidateSignedContactFileQuery("c|a|b|c", out _, out _).Should().BeFalse();
		}

		[Test]
		public void Call_file_link_validator_rejects_a_contact_file_link()
		{
			var future = DateTime.UtcNow.AddMinutes(10).Ticks;

			CallFilesController.TryValidateSignedFileQuery($"c|{DepartmentId}|42|{future}", out _, out _).Should().BeFalse("the kind marker keeps the two link spaces apart");
			CallFilesController.TryValidateSignedFileQuery($"{DepartmentId}|42|{future}", out var departmentId, out var attachmentId).Should().BeTrue();
			departmentId.Should().Be(DepartmentId);
			attachmentId.Should().Be(42);
		}

		#endregion
	}
}
