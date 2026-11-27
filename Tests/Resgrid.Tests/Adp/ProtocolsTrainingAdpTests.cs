using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Adp
{
	/// <summary>
	/// Round 8 (2026-11-25, "both systems work with ADP"): the Protocols and Training/LMS work under a department whose Advanced Data
	/// Protection is enforced (policy on, keys withheld from every unattended path). Operational data (protocol and course content, ids,
	/// statuses, scores, determinants, names) stays readable; copies of protected values (caller content, identification and licence numbers,
	/// a Records-owned signer's name, revealed Records exports) are sealed, REDACTED or never made. Synthetic canaries prove nothing leaks.
	/// </summary>
	[TestFixture]
	public class ProtocolsTrainingAdpTests
	{
		private const string Canary = "SYNTHETIC-CALLER-CANARY";

		#region Catalog 39 and bindings

		[Test]
		public void Catalog_39_holds_only_copies_of_values_adp_protects_elsewhere()
		{
			var catalog = new ProtectedFieldCatalog();
			var added = catalog.GetAll().Where(f => f.AddedInCatalogVersion == ContentProtectedFields.CatalogVersion).ToList();

			added.Select(f => f.FieldId).Should().BeEquivalentTo(new[]
			{
				"callprotocols.casejson", "callprotocols.answersjson", "callprotocols.overridereason", "callprotocols.removedreason", "callprotocolstepnotes.note",
				"signaturerequestsigners.externalname", "signaturerecords.externalsignername", "signaturecaptures.printednameencrypted", "signaturecaptures.witnessnameencrypted"
			});
			added.Where(f => f.TableName.StartsWith("CallProtocol")).Should().OnlyContain(f => f.Family == "Calls" && f.ViewPermission == PermissionTypes.ViewProtectedCallData);
			added.Where(f => !f.TableName.StartsWith("CallProtocol")).Should().OnlyContain(f => f.Family == RmsProtectedFields.Family);

			// Operational: never cataloged (run cards, dispatch texts, analytics, sampling and the signature lists read them without a grant).
			foreach (var operational in ContentProtectedFields.OperationalColumns)
			{
				var parts = operational.Split('.');
				catalog.IsProtectedField(parts[0], parts[1]).Should().BeFalse($"{operational} is operational");
			}

			// Protocol and course content, learning records, scores and evaluations are operational: none of their tables is cataloged.
			foreach (var table in new[] { "DispatchProtocols", "ProtocolVersions", "ProtocolVersionFiles", "ProtocolFileTextPages", "LmsCourseVersions", "LmsLessons", "LearningRecords",
				"LmsAssessmentAttempts", "LmsSkillEvaluations", "DispatchCaseReviews", "LmsClassEnrollments", "KnowledgeChunks", "LmsExternalLearners" })
				catalog.GetForTable(table).Should().BeEmpty($"{table} is operational (or customer-facing) data");
		}

		[Test]
		public void Catalog_39_is_bound_for_the_sweep_with_the_signature_tables_scoped_to_records_owned_requests()
		{
			var bindings = AdpTableBindings.V1.Where(b => b.Columns.Any(c => c.FieldId.StartsWith("callprotocol") || c.FieldId.StartsWith("signature"))).ToList();
			bindings.Select(b => b.TableName).Should().BeEquivalentTo("CallProtocols", "CallProtocolStepNotes", "SignatureRequestSigners", "SignatureRecords", "SignatureCaptures");

			var notes = bindings.Single(b => b.TableName == "CallProtocolStepNotes");
			notes.DepartmentColumn.Should().Be("DepartmentId");
			notes.ProtectedMarkerColumn.Should().Be("IsProtected");
			notes.Columns.Select(c => c.ColumnName).Should().Equal("Note");

			var call = bindings.Single(b => b.TableName == "CallProtocols");
			call.ParentTable.Should().Be("Calls", "a call protocol belongs to its call's department");
			call.Discriminator.Should().BeNull();
			call.Columns.Select(c => c.ColumnName).Should().BeEquivalentTo("CaseJson", "AnswersJson", "OverrideReason", "RemovedReason");

			foreach (var signature in bindings.Where(b => b.TableName.StartsWith("Signature")))
			{
				signature.ParentTable.Should().Be("SignatureRequests");
				signature.Discriminator.OnParent.Should().BeTrue();
				signature.Discriminator.Column.Should().Be("SubjectKind");
				signature.Discriminator.Integers.Should().BeEquivalentTo(new[] { (int)SignatureSubjectKinds.RecordsInspection, (int)SignatureSubjectKinds.CustodyTransfer },
					"a customer's, vendor's or outside evaluator's name is never swept");
			}

			AdpTableBindings.ForVersionRange(new ProtectedFieldCatalog(), 38, 39).Select(b => b.TableName).Should().BeEquivalentTo(bindings.Select(b => b.TableName),
				"the catalog upgrade sweep for a department pinned at 38 touches exactly these tables");
		}

		#endregion

		#region P4 case flow (CallProtocols)

		private static CallProtocol CaseRow(int id = 71) => new CallProtocol
		{
			CallProtocolId = id, CallId = 55, DispatchProtocolId = 9, Determinant = "10-D-1", Data = "10",
			CaseJson = new CallProtocolCase
			{
				ProtocolVersionId = 5, ProtocolCode = "10", ProtocolName = "Chest pain", Status = (int)ProtocolCaseStatuses.Complete, ComputedDeterminant = "10-D-1", Determinant = "10-D-1",
				Steps = new List<ProtocolCaseStep> { new ProtocolCaseStep { NodeKey = "q1", Kind = (int)ProtocolDecisionNodeKinds.Question, Text = "Age?", Answer = Canary } }
			}.ToJson(),
			AnswersJson = "{\"answers\":[{\"question\":\"Conscious?\",\"answer\":\"" + Canary + "\"}]}",
			OverrideReason = "caller says " + Canary
		};

		[Test]
		public async Task A_calls_case_content_is_sealed_by_the_write_net_and_reads_redacted_without_a_grant()
		{
			using var kit = new AdpTestKit();
			var repository = new Mock<ICallProtocolsRepository>();
			CallProtocol written = null;
			repository.Setup(x => x.UpdateProtectedContentAsync(kit.DepartmentId, It.IsAny<CallProtocol>(), It.IsAny<CancellationToken>()))
				.Callback((int d, CallProtocol r, CancellationToken _) => written = r).ReturnsAsync(true);
			var row = CaseRow();
			row.RemovedReason = "wrong protocol, " + Canary;

			(await CallProtocolProtection.ProtectAsync(kit.Seam, repository.Object, kit.DepartmentId, row, null)).Should().BeTrue();

			written.Should().BeSameAs(row);
			foreach (var accessor in ContentProtectedFields.CallProtocols)
				ProtectedDataEnvelope.HasEnvelopePrefix(accessor.Value.Get(row)).Should().BeTrue($"{accessor.Key} is caller content");
			row.Determinant.Should().Be("10-D-1", "the determinant is operational (run cards and dispatch texts read it)");

			// A page load (no grant): the call's protocols ride ResolveForReadAsync(call) and read REDACTED, never as an envelope.
			var call = new Call { CallId = 55, DepartmentId = kit.DepartmentId, Protocols = new List<CallProtocol> { row } };
			var read = await kit.Seam.ResolveForReadAsync(kit.DepartmentId, call, null, AdpTestKit.UserId);
			read.IsProtected.Should().BeTrue();
			row.CaseJson.Should().Be(ProtectedDataEnvelope.RedactionValue);
			row.AnswersJson.Should().Be(ProtectedDataEnvelope.RedactionValue);
			CallProtocolCase.Parse(row.CaseJson).Should().BeNull("a withheld case renders as no case, never a crash");
			kit.Decrypted.Should().BeEmpty();
		}

		[Test]
		public async Task A_grant_holder_reads_the_case_and_a_round_tripped_placeholder_restores_the_stored_envelope()
		{
			using var kit = new AdpTestKit();
			var repository = new Mock<ICallProtocolsRepository>();
			repository.Setup(x => x.UpdateProtectedContentAsync(It.IsAny<int>(), It.IsAny<CallProtocol>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var stored = CaseRow();
			await CallProtocolProtection.ProtectAsync(kit.Seam, repository.Object, kit.DepartmentId, stored, null);
			var sealedCase = stored.CaseJson;

			var copy = new CallProtocol { CallProtocolId = stored.CallProtocolId, CaseJson = stored.CaseJson, AnswersJson = stored.AnswersJson };
			await CallProtocolProtection.ResolveAsync(kit.Seam, kit.DepartmentId, new[] { copy }, kit.IssueGrant(), AdpTestKit.UserId);
			CallProtocolCase.Parse(copy.CaseJson).Steps.Single().Answer.Should().Be(Canary);

			var posted = new CallProtocol { CallProtocolId = stored.CallProtocolId, CaseJson = ProtectedDataEnvelope.RedactionValue };
			await CallProtocolProtection.ProtectAsync(kit.Seam, repository.Object, kit.DepartmentId, posted, stored);
			posted.CaseJson.Should().Be(sealedCase, "the sentinel restores the stored value rather than storing the word REDACTED");
		}

		[Test]
		public async Task A_department_pinned_below_39_keeps_the_case_plaintext_until_its_upgrade_sweep()
		{
			using var kit = new AdpTestKit(catalogVersion: 38);
			var row = CaseRow();
			(await CallProtocolProtection.ProtectAsync(kit.Seam, Mock.Of<ICallProtocolsRepository>(), kit.DepartmentId, row, null)).Should().BeFalse();
			row.CaseJson.Should().Contain(Canary, "a field added in a later catalog waits for the sweep (AdpTableBindings.ForVersionRange)");
		}

		[Test]
		public async Task An_override_on_a_sealed_case_needs_a_grant_and_is_written_sealed_with_a_value_free_audit()
		{
			using var kit = new AdpTestKit();
			var row = CaseRow(70);
			await CallProtocolProtection.ProtectAsync(kit.Seam, Mock.Of<ICallProtocolsRepository>(), kit.DepartmentId, row, null);
			var callProtocols = new Mock<ICallProtocolsRepository>();
			callProtocols.Setup(x => x.GetForDepartmentCallAsync(kit.DepartmentId, 55, 70)).ReturnsAsync(() => new CallProtocol
			{
				CallProtocolId = 70, CallId = 55, DispatchProtocolId = 9, Determinant = row.Determinant, CaseJson = row.CaseJson, AnswersJson = row.AnswersJson
			});
			string writtenCase = null, writtenReason = null;
			callProtocols.Setup(x => x.TryUpdateCaseAsync(kit.DepartmentId, 70, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((int d, int id, string det, string reason, string json, CancellationToken _) => { writtenCase = json; writtenReason = reason; }).ReturnsAsync(true);
			var audits = new List<AuditEvent>();
			var events = new Mock<IEventAggregator>();
			events.Setup(x => x.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => audits.Add(e));

			ProtocolCaseService Service(bool withGrant) => new ProtocolCaseService(Mock.Of<IProtocolsService>(), Mock.Of<IProtocolVersionsRepository>(), callProtocols.Object,
				new Lazy<IIntakeConversationService>(() => Mock.Of<IIntakeConversationService>()), new Lazy<ITtsAudioService>(() => Mock.Of<ITtsAudioService>()), events.Object,
				new Lazy<IProtectedReadService>(() => kit.Seam), new Lazy<IProtectedWriteService>(() => kit.Seam), kit.Attended(withGrant));

			(await Service(false).OverrideDeterminantAsync(kit.DepartmentId, 55, 70, "10-C-1", "caller now says " + Canary, AdpTestKit.UserId)).Outcome
				.Should().Be(ProtocolCaseUpdateOutcomes.ProtectedDataRequired, "the case cannot be read back without a grant, so it is never overwritten blind");
			writtenCase.Should().BeNull();

			var done = await Service(true).OverrideDeterminantAsync(kit.DepartmentId, 55, 70, "10-C-1", "caller now says " + Canary, AdpTestKit.UserId);
			done.Outcome.Should().Be(ProtocolCaseUpdateOutcomes.Done);
			done.Case.Determinant.Should().Be("10-C-1");
			ProtectedDataEnvelope.HasEnvelopePrefix(writtenCase).Should().BeTrue("the case record is written sealed");
			ProtectedDataEnvelope.HasEnvelopePrefix(writtenReason).Should().BeTrue("the override reason is written sealed");
			audits.Should().NotBeEmpty();
			audits.Should().OnlyContain(a => (a.Before ?? "").IndexOf(Canary, StringComparison.Ordinal) < 0 && (a.After ?? "").IndexOf(Canary, StringComparison.Ordinal) < 0,
				"the audit keeps ids and determinants, never the reason");
		}

		[Test]
		public async Task A_dispatch_case_reviews_gdpr_entry_never_carries_the_callers_answers()
		{
			var review = new DispatchCaseReview
			{
				DispatchCaseReviewId = 3, DepartmentId = 9, CallId = 55, DispatcherUserId = "disp", Status = (int)DispatchCaseReviewStatuses.Sent, SentOn = DateTime.UtcNow,
				ItemsJson = JsonConvert.SerializeObject(new[] { new DispatchCaseReviewItem { NodeKey = "q1", Kind = (int)ProtocolDecisionNodeKinds.Question, Text = "Age?", Answer = Canary } })
			};
			var reviews = new Mock<IDispatchCaseReviewsRepository>();
			reviews.Setup(x => x.GetSentForDispatcherAsync(9, "disp", It.IsAny<int>())).ReturnsAsync(new List<DispatchCaseReview> { review });

			var section = await new DispatchCaseReviewGdprExportSection(reviews.Object).BuildAsync("disp", 9);

			JsonConvert.SerializeObject(section).Should().NotContain(Canary, "a review made before round 8 may still hold an answer; the member's export never repeats it");
		}

		[Test]
		public void A_case_reviews_items_never_store_the_answers_and_show_them_from_the_case_as_the_reader_may_see_it()
		{
			var record = CallProtocolCase.Parse(CaseRow().CaseJson);
			var stored = DispatchCaseReviewItem.WithoutAnswers(DispatchCaseReviewItem.FromCase(record));
			DispatchCaseReviewItem.ToJson(stored).Should().NotContain(Canary);

			DispatchCaseReviewItem.FillAnswers(DispatchCaseReviewItem.Parse(DispatchCaseReviewItem.ToJson(stored)), record).Single().Answer.Should().Be(Canary);
			DispatchCaseReviewItem.FillAnswers(DispatchCaseReviewItem.Parse(DispatchCaseReviewItem.ToJson(stored)), null).Single().Answer.Should().BeNull("a withheld case shows no answers");
		}

		#endregion

		[Test]
		public void The_operational_paths_of_both_systems_never_touch_protected_data_so_adp_cannot_change_how_they_run()
		{
			// Library, acknowledgments, public pages, the exchange, courses, classes and rosters, recorded completions, requirements, the
			// transcript, external learners, skill evaluations: protocol and course content, ids, statuses, scores and names are operational
			// (2026-10-17) or customer-facing/external. None of these services takes the protected seam or the protection state, so a department
			// with ADP enforced runs them exactly as any other (no grant prompt, no REDACTED, no crash), and none of their tables is cataloged.
			var seamTypes = new[] { typeof(IProtectedReadService), typeof(IProtectedWriteService), typeof(IDepartmentDataProtectionService), typeof(IProtectedGrantContext) };
			foreach (var service in new[]
			{
				typeof(ProtocolLibraryService), typeof(ProtocolAcknowledgmentService), typeof(ProtocolPublicPageService), typeof(CommunityExchangeService), typeof(ProtocolAccessService),
				typeof(LmsCourseService), typeof(LmsClassService), typeof(LmsCompletionRecordingService), typeof(TrainingCompletionRecordingService), typeof(LmsRequirementService),
				typeof(LearningRecordService), typeof(LmsExternalLearnerService), typeof(LmsLearnerService), typeof(LmsPublicClassService), typeof(LmsSkillEvaluationService)
			})
				foreach (var constructor in service.GetConstructors())
					constructor.GetParameters().Select(p => p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(Lazy<>) ? p.ParameterType.GetGenericArguments()[0] : p.ParameterType)
						.Should().NotContain(seamTypes, $"{service.Name} runs on operational data only");
		}

		#region Training/LMS

		[Test]
		public async Task Ce_and_data_exit_members_carry_the_department_scoped_number_through_the_protected_pipeline_never_the_legacy_column()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetAllMembersForDepartmentIncludingDeletedAsync(9)).ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = 9, UserId = "m1" } });
			departments.Setup(x => x.GetAllUsersForDepartmentUnlimitedAsync(9, It.IsAny<bool>())).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser>());
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetAllProfilesForDepartmentIncDisabledDeletedAsync(9)).ReturnsAsync(new Dictionary<string, UserProfile>
			{
				["m1"] = new UserProfile { UserId = "m1", FirstName = "Pat", IdentificationNumber = "LEGACY-PLAINTEXT-" + Canary }
			});
			var sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
			sensitive.Setup(x => x.ApplyIdentificationNumbersAsync(9, It.IsAny<IEnumerable<UserProfile>>(), null, null))
				.Callback((int d, IEnumerable<UserProfile> list, string g, string u) => { foreach (var p in list) p.IdentificationNumber = ProtectedDataEnvelope.RedactionValue; })
				.Returns(Task.CompletedTask);

			var members = await LmsExportMembers.LoadAsync(departments.Object, profiles.Object, sensitive.Object, 9, null, null, null);
			members.Single().IdentificationNumber.Should().Be(ProtectedDataEnvelope.RedactionValue, "an unattended export holds no grant");

			var none = await LmsExportMembers.LoadAsync(departments.Object, profiles.Object, null, 9, null, null, null);
			none.Single().IdentificationNumber.Should().BeNull("the legacy global column is never read raw");
		}

		[Test]
		public void Learning_records_from_records_training_and_building_familiarization_copy_no_protected_records_text()
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = "r1", DepartmentId = 9, RecordNumber = "TR-26-4", DisplaySummary = "Ladder drill", StartedOn = DateTime.UtcNow.AddHours(-2), EndedOn = DateTime.UtcNow
			};
			var drafts = LearningRecordWriter.RecordsDrafts(record, new[] { new RmsRecordParticipant { UserId = "m1" } }, false);
			drafts.Should().OnlyContain(d => d.Notes == null && d.Objectives == null && d.Location == null && d.InstructorNames == null,
				"only the record's operational number, summary (Standard fields) and times reach the transcript");

			var draft = LmsFamiliarizationCreditService.Draft(new LmsFamiliarizationVisit { SourceRefId = "inspection:1", Kind = "Inspection", Reference = "INS-26-1", UserId = "m1", CompletedOn = DateTime.UtcNow },
				new LmsProgramSettings { FamiliarizationCategoryId = 3, FamiliarizationMinutes = 30 });
			draft.Notes.Should().BeNull();
			draft.Location.Should().BeNull();
			draft.Title.Should().Be("Building familiarization: inspection INS-26-1", "the inspection number is operational; no occupancy text is copied");
		}

		#endregion
	}
}
