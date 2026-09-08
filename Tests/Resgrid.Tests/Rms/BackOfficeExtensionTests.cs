using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The seven RMS extensions the Incident Back Office plan enumerates in section 10A.4. Each is small on its own;
	/// what these tests hold is that they behave as that plan's contract assumes, so the program can be built against
	/// them without discovering later that a subject type is refused, a number is department-scoped, a non-NERIS
	/// submission is dispatched to NERIS, or a Web-only definition reaches a phone.
	/// </summary>
	[TestFixture]
	public class BackOfficeExtensionTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsDefinitionHarness();

		private static RecordDefinitionSchema Simple() => Schema(Section("main", "Main", Field("summary", RmsFieldType.ShortText, true)));

		// ---- E1: subject reference types ------------------------------------------------------------------------

		[Test]
		public async Task E1_incident_subject_types_are_accepted_and_an_unknown_subject_is_still_refused()
		{
			var incidentSubjects = new[]
			{
				"incidentcommand", "incidentoperationalperiod", "incidentparticipant",
				"incidentresource", "incidentfacility", "incidentresourcerequest", "vendor"
			};

			foreach (var subject in incidentSubjects)
			{
				var validation = await _h.Definitions.ValidateAsync(Dept, new RecordDefinitionDraftInput
				{
					Name = "Subject " + subject, Schema = Simple(), PermittedSubjectTypes = "call," + subject,
					Numbering = new RecordDefinitionNumbering { Prefix = "SUB" }
				});
				validation.Issues.Where(i => i.Severity == "error").Should().BeEmpty($"'{subject}' is a supported subject type");
			}

			var all = await _h.Definitions.ValidateAsync(Dept, new RecordDefinitionDraftInput
			{
				Name = "Every incident subject", Schema = Simple(), PermittedSubjectTypes = "call," + string.Join(",", incidentSubjects),
				Numbering = new RecordDefinitionNumbering { Prefix = "SUB" }
			});
			all.Issues.Where(i => i.Severity == "error").Should().BeEmpty();

			var unknown = await _h.Definitions.ValidateAsync(Dept, new RecordDefinitionDraftInput
			{
				Name = "Bad subject", Schema = Simple(), PermittedSubjectTypes = "call,spaceship",
				Numbering = new RecordDefinitionNumbering { Prefix = "SUB" }
			});
			unknown.Issues.Should().Contain(i => i.Code == "unknown_subject" && i.Severity == "error");
		}

		// ---- E2: incident-scoped numbering ----------------------------------------------------------------------

		[Test]
		public async Task E2_incident_scoped_numbering_restarts_on_each_call_and_falls_back_without_one()
		{
			foreach (var callId in new[] { 4100, 4200 })
				_h.Calls.Setup(c => c.GetCallByIdAsync(callId, It.IsAny<bool>()))
					.ReturnsAsync(new Call { CallId = callId, DepartmentId = Dept, Number = "C-" + callId, Name = "Incident " + callId, Type = "Fire", LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc) });

			await _h.CreateAndPublishAsync("ics-214", "Activity log", Simple(), d =>
			{
				d.PermittedSubjectTypes = "call,incidentcommand";
				d.Numbering = new RecordDefinitionNumbering { Prefix = "ICS214", Assignment = RmsNumberAssignment.OnCreate, SequenceWidth = 3, PerIncidentSequence = true, ResetYearly = true };
			});

			var first = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-214", CallId = 4100 });
			var second = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-214", CallId = 4100 });
			var otherIncident = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-214", CallId = 4200 });

			first.Record.RecordNumber.Should().Be("ICS214-C4100-001");
			second.Record.RecordNumber.Should().Be("ICS214-C4100-002", "the sequence counts within the incident");
			otherIncident.Record.RecordNumber.Should().Be("ICS214-C4200-001", "a different incident starts its own sequence");

			// Incident scope replaces the year segment: the sequence resets with the incident, not the calendar.
			first.Record.RecordNumber.Should().NotContain(DateTime.UtcNow.Year.ToString());

			var noCall = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-214" });
			noCall.Record.RecordNumber.Should().Be("ICS214-" + DateTime.UtcNow.Year + "-001", "a Record with no Call keeps the wider department scope rather than colliding");
		}

		[Test]
		public async Task E2_incident_scoped_numbering_without_the_call_subject_warns_but_does_not_block()
		{
			var validation = await _h.Definitions.ValidateAsync(Dept, new RecordDefinitionDraftInput
			{
				Name = "Incident scoped, no call subject", Schema = Simple(), PermittedSubjectTypes = "unit",
				Numbering = new RecordDefinitionNumbering { Prefix = "ICS", PerIncidentSequence = true }
			});

			validation.Issues.Where(i => i.Severity == "error").Should().BeEmpty();
			validation.Issues.Should().Contain(i => i.Code == "no_call_subject" && i.Severity == "warning");
		}

		// ---- Per-definition cardinality (plan 5.2.1) ------------------------------------------------------------

		private void RegisterCall(int callId) => _h.Calls.Setup(c => c.GetCallByIdAsync(callId, It.IsAny<bool>()))
			.ReturnsAsync(new Call { CallId = callId, DepartmentId = Dept, Number = "C-" + callId, Name = "Incident " + callId, Type = "Fire", LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc) });

		[Test]
		public async Task SingleAuthoritative_allows_one_record_per_call_and_hands_back_the_one_that_exists()
		{
			RegisterCall(5100);
			RegisterCall(5200);
			await _h.CreateAndPublishAsync("ics-209", "Status summary", Simple(), d =>
			{
				d.PermittedSubjectTypes = "call,incidentcommand";
				d.Cardinality = RmsRecordCardinality.SingleAuthoritative;
			});

			var first = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-209", CallId = 5100 });
			first.Record.CardinalityKey.Should().Be("single:5100:ics-209");

			Func<Task> second = () => _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-209", CallId = 5100 });
			var thrown = await second.Should().ThrowAsync<RecordCardinalityException>();
			thrown.Which.ExistingRecordId.Should().Be(first.Record.RmsOperationalRecordId, "the author is pointed at the Record that exists, not shown an error");
			thrown.Which.Cardinality.Should().Be(RmsRecordCardinality.SingleAuthoritative);

			var otherCall = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-209", CallId = 5200 });
			otherCall.Record.CardinalityKey.Should().Be("single:5200:ics-209", "the rule is per Call, not per department");

			var noCall = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-209" });
			noCall.Record.CardinalityKey.Should().BeNull("every rule is keyed on the Call; without one there is nothing to enforce");
		}

		[Test]
		public async Task A_cancelled_or_voided_record_releases_its_slot()
		{
			RegisterCall(5300);
			await _h.CreateAndPublishAsync("ics-209", "Status summary", Simple(), d =>
			{
				d.PermittedSubjectTypes = "call";
				d.Cardinality = RmsRecordCardinality.SingleAuthoritative;
			});

			var first = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-209", CallId = 5300 });
			var cancelled = await _h.Records.CancelAsync(Dept, Author, first.Record.RmsOperationalRecordId);
			cancelled.Record.CardinalityKey.Should().BeNull();

			var replacement = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-209", CallId = 5300 });
			replacement.Record.CardinalityKey.Should().Be("single:5300:ics-209", "an abandoned Record must not block the one that replaces it");
		}

		[Test]
		public async Task OnePerSubjectPerCall_keys_on_the_unit_and_falls_back_to_the_author()
		{
			RegisterCall(5400);
			await _h.CreateAndPublishAsync("unit-report", "Unit report", Simple(), d =>
			{
				d.PermittedSubjectTypes = "call,unit";
				d.Cardinality = RmsRecordCardinality.OnePerSubjectPerCall;
			});

			var engine1 = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "unit-report", CallId = 5400, Details = new RmsOperationalRecordDetail { UnitId = 11 }
			});
			var engine2 = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "unit-report", CallId = 5400, Details = new RmsOperationalRecordDetail { UnitId = 12 }
			});

			engine1.Record.CardinalityKey.Should().Be("subject:5400:unit-report:unit:11");
			engine2.Record.CardinalityKey.Should().Be("subject:5400:unit-report:unit:12", "two engine companies on one fire produce two company-level records");

			Func<Task> duplicate = () => _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "unit-report", CallId = 5400, Details = new RmsOperationalRecordDetail { UnitId = 11 }
			});
			(await duplicate.Should().ThrowAsync<RecordCardinalityException>()).Which.ExistingRecordId.Should().Be(engine1.Record.RmsOperationalRecordId);

			// With no unit named, the subject is the author — which is what a per-person record is keyed on.
			var self = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "unit-report", CallId = 5400 });
			self.Record.CardinalityKey.Should().Be("subject:5400:unit-report:person:" + Author);
		}

		[Test]
		public async Task MultiplePerCall_is_the_default_and_constrains_nothing()
		{
			RegisterCall(5500);
			await _h.CreateAndPublishAsync("ics-214", "Activity log", Simple(), d => d.PermittedSubjectTypes = "call");
			_h.Version("ics-214", 1).Cardinality.Should().Be((int)RmsRecordCardinality.MultiplePerCall, "a definition that says nothing allows multiples");

			for (var i = 0; i < 3; i++)
				(await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "ics-214", CallId = 5500 }))
					.Record.CardinalityKey.Should().BeNull();
		}

		[Test]
		public async Task A_cardinality_rule_without_the_call_subject_is_refused_and_a_tightening_change_is_breaking()
		{
			var validation = await _h.Definitions.ValidateAsync(Dept, new RecordDefinitionDraftInput
			{
				Name = "Single, no call subject", Schema = Simple(), PermittedSubjectTypes = "unit",
				Cardinality = RmsRecordCardinality.SingleAuthoritative, Numbering = new RecordDefinitionNumbering { Prefix = "SNG" }
			});
			validation.Issues.Should().Contain(i => i.Code == "no_call_subject" && i.Severity == "error",
				"a rule keyed on the Call that can never see one is a rule that never fires");

			await _h.CreateAndPublishAsync("tightening", "Tightening", Simple(), d => d.PermittedSubjectTypes = "call");
			var draft = await _h.Definitions.OpenDraftAsync(Dept, Admin, "tightening");
			var input = RecordDefinitionsService.ToDraftInput(draft, (await _h.Definitions.GetAsync(Dept, "tightening")).Definition);
			input.Cardinality = RmsRecordCardinality.SingleAuthoritative;
			await _h.Definitions.SaveDraftAsync(Dept, Admin, "tightening", draft.Version, draft.RowVersion, input);

			var diff = await _h.Definitions.DiffAsync(Dept, "tightening", 1, 2);
			diff.Entries.Should().Contain(e => e.Key == "cardinality" && e.Breaking,
				"Records legal under the old rule already exist, so tightening is a breaking change the publisher must see");
		}

		// ---- E3: external identifier schemes --------------------------------------------------------------------

		[Test]
		public void E3_the_incident_business_identifier_schemes_are_named_and_used_by_the_pack()
		{
			RmsExternalReferenceSchemes.All.Should().Contain(new[]
			{
				RmsExternalReferenceSchemes.Iroc, RmsExternalReferenceSchemes.EIsuite, RmsExternalReferenceSchemes.Emac,
				RmsExternalReferenceSchemes.WebEoc, RmsExternalReferenceSchemes.Lscms, RmsExternalReferenceSchemes.NfesIclip,
				RmsExternalReferenceSchemes.LodgingConfirmation, RmsExternalReferenceSchemes.VendorInvoice, RmsExternalReferenceSchemes.FinancePosting
			});
			RmsExternalReferenceSchemes.All.Should().OnlyHaveUniqueItems();

			var packSchemes = RecordTemplateCatalog.Packs.Single(p => p.Key == RecordTemplateCatalog.IncidentSupportPackKey)
				.Definitions.SelectMany(d => d.Schema.Sections).SelectMany(s => s.Fields)
				.Where(f => f.Type == RmsFieldType.ExternalReference).Select(f => f.ReferenceType).Distinct().ToList();

			packSchemes.Should().Contain(new[]
			{
				RmsExternalReferenceSchemes.Iroc, RmsExternalReferenceSchemes.EIsuite, RmsExternalReferenceSchemes.Emac,
				RmsExternalReferenceSchemes.WebEoc, RmsExternalReferenceSchemes.Lscms, RmsExternalReferenceSchemes.NfesIclip,
				RmsExternalReferenceSchemes.VendorInvoice, RmsExternalReferenceSchemes.FinancePosting
			});
		}

		// ---- E4: non-NERIS submission destinations --------------------------------------------------------------

		[Test]
		public void E4_non_neris_destinations_exist_and_are_not_owned_by_the_neris_worker()
		{
			var nonNeris = new[]
			{
				RmsSubmissionDestinations.FinanceExport, RmsSubmissionDestinations.EIsuiteExchange,
				RmsSubmissionDestinations.EmacReimbursement, RmsSubmissionDestinations.AgencyRecordsFiling
			};

			foreach (var destination in nonNeris)
			{
				RmsSubmissionDestinations.IsKnown(destination).Should().BeTrue($"{destination} is a destination RMS recognizes");
				RmsSubmissionDestinations.IsNerisOwned(destination).Should().BeFalse($"worker 41 speaks NERIS and must not claim {destination}");
			}

			RmsSubmissionDestinations.NerisOwned.Should().BeEquivalentTo(new[] { RmsSubmissionDestinations.Neris, RmsSubmissionDestinations.NerisIncidentAnalysis });
			RmsSubmissionDestinations.All.Should().OnlyHaveUniqueItems().And.HaveCount(6);
			RmsSubmissionDestinations.IsKnown("SOMETHING_ELSE").Should().BeFalse();
		}

		[Test]
		public void E4_the_submission_row_carries_a_non_neris_exchange_without_any_neris_field()
		{
			// The point of E4: the submission model is a general outbound-exchange record. A finance export fills the
			// same columns as a NERIS filing and needs no NERIS profile, contract version or entity identity.
			var submission = new RmsSubmission
			{
				RmsSubmissionId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				RecordId = "record-1",
				RecordKind = (int)RmsRecordKind.Operational,
				RevisionId = "revision-1",
				Destination = RmsSubmissionDestinations.FinanceExport,
				DestinationVersion = "1.0",
				IdempotencyKey = "record-1:revision-1:finance",
				State = (int)RmsSubmissionState.Queued,
				PayloadJson = "{}",
				PayloadChecksum = RecordSnapshotSerializer.Checksum("{}"),
				MaxAttempts = 3,
				QueuedOn = DateTime.UtcNow
			};

			submission.DestinationIdentity.Should().BeNull("a non-NERIS destination has no NERIS entity identity");
			RmsSubmissionDestinations.IsKnown(submission.Destination).Should().BeTrue();
			RmsSubmissionDestinations.IsNerisOwned(submission.Destination).Should().BeFalse();
			submission.PayloadChecksum.Should().NotBeNullOrWhiteSpace("the payload is checksummed whatever the destination");
		}

		// ---- E5/E6: the Incident Support pack ---------------------------------------------------------------------

		[Test]
		public async Task E5_the_incident_support_pack_ships_every_group_web_only_incident_scoped_and_preview()
		{
			// Read the shipped catalog directly: GetCatalogAsync also mirrors the catalog into the product-scope
			// tables behind a process-wide one-shot latch, and RecordTemplateCatalogTests owns the assertion that
			// the mirror runs exactly once.
			var pack = RecordTemplateCatalog.Packs.Single(p => p.Key == RecordTemplateCatalog.IncidentSupportPackKey);

			pack.IsPreview.Should().BeTrue("no agency has accepted output from this pack yet");
			pack.SupportedProfiles.Should().BeEquivalentTo(new[] { "generic", "us-nwcg", "us-nims", "us-calif", "ca" });
			pack.Sources.Should().NotBeEmpty();
			pack.Definitions.Should().HaveCountGreaterThan(40);

			// Every group in the plan's section 10A.2 table is represented.
			var keys = pack.Definitions.Select(d => d.Key).ToList();
			keys.Should().Contain(new[]
			{
				// IAP core and the assembled plan
				"pack.incident-support.ics-202-objectives", "pack.incident-support.ics-203-organization",
				"pack.incident-support.ics-204-assignment", "pack.incident-support.ics-207-org-chart", "pack.incident-support.iap-package",
				// Status reporting
				"pack.incident-support.ics-209-status-summary",
				// Communications
				"pack.incident-support.ics-205-comms-plan", "pack.incident-support.ics-205a-comms-list", "pack.incident-support.ics-217a-frequency-inventory",
				// Medical and safety
				"pack.incident-support.ics-206-medical-plan", "pack.incident-support.ics-208-safety-message", "pack.incident-support.ics-215a-hazard-analysis",
				// Resources
				"pack.incident-support.ics-210-status-change", "pack.incident-support.ics-211-check-in", "pack.incident-support.ics-213-general-message",
				"pack.incident-support.ics-213rr-resource-request", "pack.incident-support.ics-218-support-vehicle-inventory",
				"pack.incident-support.ics-219-tcard", "pack.incident-support.ics-221-demobilization",
				// Activity and planning
				"pack.incident-support.ics-214-activity-log", "pack.incident-support.ics-215-planning-worksheet",
				"pack.incident-support.ics-220-air-operations", "pack.incident-support.ics-225-performance-rating", "pack.incident-support.ics-260-resource-order",
				// Incident business
				"pack.incident-support.sf-261-crew-time", "pack.incident-support.of-286-equipment-use-invoice",
				"pack.incident-support.of-288-firefighter-time", "pack.incident-support.of-294-equipment-shift-ticket",
				"pack.incident-support.of-296-equipment-inspection", "pack.incident-support.of-297-rental-use-envelope",
				"pack.incident-support.of-315-rental-agreement",
				// Support operations
				"pack.incident-support.facility-inspection", "pack.incident-support.facility-use-agreement",
				"pack.incident-support.camp-sanitation-inspection", "pack.incident-support.food-service-inspection",
				"pack.incident-support.potable-water-test", "pack.incident-support.shift-ticket",
				"pack.incident-support.delivery-receiving-ticket", "pack.incident-support.corrective-action",
				"pack.incident-support.incident-accident-report",
				// Business administration
				"pack.incident-support.delegation-of-authority", "pack.incident-support.funding-authorization",
				"pack.incident-support.cost-share-agreement", "pack.incident-support.incident-business-summary",
				"pack.incident-support.purchase-justification", "pack.incident-support.conflict-of-interest-attestation"
			});

			foreach (var template in pack.Definitions)
			{
				template.ClientSurface.IsWebOnly.Should().BeTrue($"{template.Key} is a desk product (E7)");
				template.ClientSurface.AllowOffline.Should().BeFalse($"{template.Key} is never written to a device");
				template.PerIncidentSequence.Should().BeTrue($"{template.Key} numbers per incident (E2)");
				template.PermittedSubjectTypes.Should().Contain("call");
				template.PermittedSubjectTypes.Should().Contain("incidentcommand", "the incident is the subject these records hang from (E1)");
			}
		}

		[Test]
		public async Task E6_incident_business_records_carry_the_incident_business_category()
		{
			var pack = RecordTemplateCatalog.Packs.Single(p => p.Key == RecordTemplateCatalog.IncidentSupportPackKey);

			var business = pack.Definitions.Where(d => d.Category == RecordDefinitionCategories.IncidentBusiness).Select(d => d.Key).ToList();
			business.Should().Contain(new[]
			{
				"pack.incident-support.sf-261-crew-time", "pack.incident-support.of-286-equipment-use-invoice",
				"pack.incident-support.of-315-rental-agreement", "pack.incident-support.delegation-of-authority",
				"pack.incident-support.funding-authorization", "pack.incident-support.cost-share-agreement",
				"pack.incident-support.incident-business-summary", "pack.incident-support.purchase-justification",
				"pack.incident-support.conflict-of-interest-attestation", "pack.incident-support.facility-use-agreement"
			});

			pack.Definitions.Single(d => d.Key == "pack.incident-support.ics-202-objectives").Category
				.Should().Be(RecordDefinitionCategories.IncidentSupport, "planning products are support, not business");

			// The category survives a clone, which is what makes it useful for grouping a department's own copies.
			var aggregate = await _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput
			{
				DefinitionKey = "crew-time", Name = "Crew time report", TemplateKey = "pack.incident-support.sf-261-crew-time", JurisdictionProfileKey = "us-nwcg"
			});
			aggregate.Definition.Category.Should().Be(RecordDefinitionCategories.IncidentBusiness);
			aggregate.Latest.Numbering.PerIncidentSequence.Should().BeTrue("the clone inherits incident-scoped numbering from the template");
		}

		[Test]
		public async Task E5_ledger_owned_figures_carry_a_reference_and_a_restricted_floor()
		{
			// Section 10A.0: the ledger is authoritative. Where a pack record shows a cost or an hour count it also
			// carries the reference the figure came from, and the figure itself is not department-wide reading.
			var invoice = await _h.Templates.RenderAsync("pack.incident-support.of-286-equipment-use-invoice", "us-nwcg", null);
			invoice.Schema.FindField("claimed_total").Classification.Should().Be(RmsFieldClassification.Restricted);
			invoice.Schema.FindField("claimed_total").Aggregatable.Should().BeFalse("a restricted figure loses its safe projection with its floor");
			invoice.Schema.FindField("ledger_reference").Type.Should().Be(RmsFieldType.ExternalReference);
			invoice.Schema.FindField("finance_reference").ReferenceType.Should().Be(RmsExternalReferenceSchemes.FinancePosting);

			var crewTime = await _h.Templates.RenderAsync("pack.incident-support.sf-261-crew-time", "us-nwcg", null);
			crewTime.Schema.FindField("hours_worked").Classification.Should().Be(RmsFieldClassification.Restricted);
			crewTime.Schema.FindField("ledger_reference").Should().NotBeNull();

			var accident = _h.Templates.GetTemplate("pack.incident-support.incident-accident-report");
			accident.RetentionYears.Should().Be(0, "casualty and exposure records are retained permanently");
			var rendered = await _h.Templates.RenderAsync(accident.Key, "generic", null);
			rendered.Schema.FindField("injury_description").Classification.Should().Be(RmsFieldClassification.Restricted);
			rendered.Schema.FindField("incident_name").Classification.Should().Be(RmsFieldClassification.Standard, "a floor touches only the fields its policy names");
		}

		[Test]
		public async Task E5_the_incident_support_profiles_relabel_without_changing_the_base_template()
		{
			var nwcg = await _h.Templates.RenderAsync("pack.incident-support.ics-211-check-in", "us-nwcg", "en-US");
			var nims = await _h.Templates.RenderAsync("pack.incident-support.ics-211-check-in", "us-nims", "en-US");
			var california = await _h.Templates.RenderAsync("pack.incident-support.ics-211-check-in", "us-calif", "en-US");
			var canada = await _h.Templates.RenderAsync("pack.incident-support.ics-211-check-in", "ca", "en-CA");

			nwcg.Schema.FindField("position").Label.Should().Be("ICS position (PMS 310-1)");
			nims.Schema.FindField("position").Label.Should().Be("ICS position (NIMS)");
			california.Schema.FindField("position").Label.Should().Be("ICS position (CICCS)");
			RecordTemplateCatalog.Find("pack.incident-support.ics-211-check-in").Schema.FindField("position").Label
				.Should().Be("ICS position", "rendering deep-copies the schema");

			nwcg.MeasurementSystem.Should().Be("customary");
			canada.MeasurementSystem.Should().Be("metric");
			canada.CurrencyCode.Should().Be("CAD");

			// "us-calif" is California; "us-ca" remains the U.S.-Canada cross-border pair the mutual-aid pack uses.
			RecordTemplateCatalog.FindProfile("us-calif").Subdivision.Should().Be("CA");
			RecordTemplateCatalog.FindProfile("us-ca").Country.Should().Be("US-CA");
			foreach (var profile in new[] { "us-nwcg", "us-nims", "us-calif" })
				RecordTemplateCatalog.FindProfile(profile).ArtifactStatus.Should().Be((int)RmsArtifactStatus.Compatible, "nothing here is an exact named form");

			var unsupported = () => _h.Templates.RenderAsync("pack.incident-support.ics-211-check-in", "us", null);
			await unsupported.Should().ThrowAsync<ArgumentException>().WithMessage("*does not support profile*");
		}

		// ---- E7: the Web-only client surface --------------------------------------------------------------------

		[Test]
		public async Task E7_a_web_only_definition_is_refused_by_every_field_app_and_stays_web_authorable()
		{
			var surface = RecordDefinitionClientSurface.WebOnly();
			surface.IsWebOnly.Should().BeTrue();
			surface.AllowOffline.Should().BeFalse();
			new RecordDefinitionClientSurface { Dispatch = true }.IsWebOnly.Should().BeFalse("one app is enough to stop it being Web only");

			var field = new WebOnlyCatalogFixture();
			field.Publish("desk-product", surface);
			field.Publish("phone-product", new RecordDefinitionClientSurface
			{
				Responder = true, Unit = true, IncidentCommand = true, Dispatch = true,
				LaunchContexts = { FieldRecordCatalogV1.LaunchContexts.None }
			});

			foreach (var origin in new[] { RmsOriginClient.Responder, RmsOriginClient.Unit, RmsOriginClient.IncidentCommand, RmsOriginClient.Dispatch })
			{
				var catalog = await field.CatalogAsync(origin);
				catalog.Definitions.Select(d => d.DefinitionKey).Should().NotContain("desk-product", $"{origin} may not author a Web-only definition");
				catalog.Definitions.Select(d => d.DefinitionKey).Should().Contain("phone-product");
				catalog.Exclusions.Should().Contain(e => e.DefinitionKey == "desk-product" && e.Reason == FieldRecordCatalogV1.ExclusionReasons.SurfaceNotEnabled,
					$"{origin} is told why, not left to guess");
			}

			// The Web renderer is not gated by the client surface, so the same definition authors normally there.
			await _h.CreateAndPublishAsync("desk-product", "Desk product", Simple(), d => d.ClientSurface = RecordDefinitionClientSurface.WebOnly());
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "desk-product", OriginClient = RmsOriginClient.Web,
				Values = new List<RecordValueInput> { Value("main", "summary", "Authored at a desk") }
			});
			draft.Record.RmsOperationalRecordId.Should().NotBeNullOrEmpty();
			draft.Record.DefinitionKey.Should().Be("desk-product");
			draft.Values.Scalar("summary").Display.Should().Be("Authored at a desk");
		}

		/// <summary>A minimal FieldRecordsService around two published definitions, for the surface-exclusion matrix.</summary>
		private sealed class WebOnlyCatalogFixture
		{
			private const int Department = 9;
			private readonly List<RmsRecordDefinitionVersion> _published = new List<RmsRecordDefinitionVersion>();
			private readonly List<RecordDefinitionSummary> _summaries = new List<RecordDefinitionSummary>();
			private readonly FieldRecordsService _service;

			public WebOnlyCatalogFixture()
			{
				var cutover = new Mock<IRecordsCutoverService>();
				cutover.Setup(c => c.GetModuleStateAsync(Department, It.IsAny<bool>()))
					.ReturnsAsync(new RecordsModuleState { DepartmentId = Department, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });

				var authorization = new Mock<IRecordsAuthorizationService>();
				authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Department)).ReturnsAsync(true);
				authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Department, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
				authorization.Setup(a => a.GetReadScopeStampAsync(It.IsAny<string>(), Department)).ReturnsAsync("scope-1");
				authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Department)).ReturnsAsync((List<int>)null);

				var flags = new Mock<IFeatureToggleService>();
				flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), Department, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

				var definitions = new Mock<IRecordDefinitionsService>();
				definitions.Setup(d => d.GetPublishedAsync(Department)).ReturnsAsync(() => _published);
				definitions.Setup(d => d.ListAsync(Department, It.IsAny<bool>())).ReturnsAsync(() => _summaries);

				var protection = new Mock<IDepartmentDataProtectionService>();
				protection.Setup(p => p.GetPolicyByDepartmentIdAsync(Department, It.IsAny<bool>())).ReturnsAsync((DepartmentDataProtectionPolicy)null);

				_service = new FieldRecordsService(cutover.Object, authorization.Object, flags.Object, definitions.Object, protection.Object,
					Mock.Of<IRecordsService>(), Mock.Of<IRecordWorkAssignmentsService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentGroupsService>(),
					Mock.Of<ICallsService>(), Mock.Of<IIncidentCommandService>(), Mock.Of<IRecordsFieldRolloutService>());
			}

			public void Publish(string key, RecordDefinitionClientSurface surface)
			{
				var schema = Schema(Section("main", "Main", Field("summary", RmsFieldType.ShortText, true)));
				_published.Add(new RmsRecordDefinitionVersion
				{
					DepartmentId = Department, DefinitionKey = key, Version = 1, State = (int)RmsDefinitionVersionState.Published,
					LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry, Schema = schema, ClientSurface = surface,
					SchemaChecksum = "chk-" + key, MinimumClientCapability = RecordsClientCapabilities.Derive(schema)
				});
				_summaries.Add(new RecordDefinitionSummary { Key = key, Name = key, Category = "Operations", PublishedVersion = 1 });
			}

			public Task<FieldRecordCatalog> CatalogAsync(RmsOriginClient origin)
				=> _service.GetCatalogAsync(Department, "member", new FieldRecordCatalogRequest
				{
					Origin = origin, ClientCapability = RecordsClientCapabilities.Packs, AppVersion = "9.9.9", Context = new FieldRecordContext()
				});
		}
	}
}
