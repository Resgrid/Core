using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The 2026-09-19 completion pass over the Workforce &amp; Business Operations phases: every lifecycle mutation reaches
	/// the Workflow Engine (triggers 180-187 join 52-57/94-95, 81-86 and 23/87-93), and Advanced Data Protection covers
	/// only what stays inside the department (deployment notes, certification status reasons; catalog 27). Everything a
	/// customer reads without a login — invoices, receipts, bids, contracts, compliance documents, daily time reports,
	/// expenses and attachments — is stored whole so the pay page, bid e-mail and invoice packet render for them.
	/// </summary>
	[TestFixture]
	public class WorkforceProtectionAndEventsTests
	{
		private const int DeptId = 7;

		private sealed class Grant : IProtectedGrantContext
		{
			public string GrantToken { get; set; }
			public string UserId { get; set; }
			public bool IsWorkloadCaller => UserId == null;
		}

		#region Catalog and bindings

		[Test]
		public void Catalog_27_keeps_only_internal_fields_and_binds_no_customer_facing_table()
		{
			var catalog = new ProtectedFieldCatalog();
			catalog.Version.Should().Be(27, "Phase D rides 26 and the completion pass 27; nothing is deployed on either yet");
			catalog.GetAll().Where(f => f.AddedInCatalogVersion == 27).Select(f => f.FieldId).Should().BeEquivalentTo(new[]
			{
				"personnelcertifications.statusreason", "unitcertifications.statusreason", "deployments.notes"
			});
			catalog.GetAll().Where(f => f.AddedInCatalogVersion == 26).Select(f => f.FieldId).Should().Contain(new[] { "unitcertifications.notes", "unitcertifications.data", "personnelcertificationcredits.description" });

			// Customers read these without a login (pay page, bid e-mail, invoice packet): none of it may sit under ADP.
			var customerFacing = new[]
			{
				"Invoices", "InvoiceLineItems", "InvoicePayments", "CustomerBillingProfiles", "DepartmentBillingIdentities",
				"Bids", "BidLineItems", "ServiceContracts", "DepartmentComplianceDocuments",
				"DeploymentTimeReports", "DeploymentTimeEntries", "DeploymentExpenses", "DeploymentAttachments"
			};
			foreach (var table in customerFacing)
			{
				catalog.GetAll().Should().NotContain(f => string.Equals(f.TableName, table, StringComparison.OrdinalIgnoreCase), table);
				AdpTableBindings.V1.Should().NotContain(b => string.Equals(b.TableName, table, StringComparison.OrdinalIgnoreCase), table);
			}
			foreach (var column in AdpTableBindings.V1.SelectMany(b => b.Columns))
				catalog.GetById(column.FieldId).Should().NotBeNull(column.FieldId);
			AdpTableBindings.V1.Single(b => b.TableName == "Deployments").Columns.Select(c => c.FieldId).Should().BeEquivalentTo(new[] { "deployments.notes" });
			// The accessor maps drive the seams that remain.
			DeploymentProtectedFields.DeploymentFields.Keys.Should().BeEquivalentTo(new[] { "deployments.notes" });
			DeploymentProtectedFields.All().Should().ContainSingle();
			ProtectedReadService.CertificationFieldAccessors.Keys.Should().Contain("personnelcertifications.statusreason");
			CertificationProtectedFields.Unit.Keys.Should().Contain("unitcertifications.statusreason");
		}

		[Test]
		public void Workload_purpose_allow_list_covers_invoice_delivery()
		{
			Resgrid.Config.DataProtectionConfig.BrokerWorkloadPurposes.Split(',').Select(p => p.Trim()).Should().Contain("invoicing",
				"invoice, receipt and bid renders decrypt the customer's Contact row through this lane; a missing purpose fails closed and would print REDACTED as the bill-to name");
		}

		#endregion

		#region Webhook ledger

		[Test]
		public void Webhook_ledger_payload_keeps_reconciliation_ids_and_drops_payer_identity()
		{
			var raw = "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":{\"id\":\"cs_1\",\"amount_total\":121500,\"currency\":\"usd\",\"payment_intent\":\"pi_1\",\"customer\":\"cus_1\",\"customer_details\":{\"email\":\"payer@example.com\",\"name\":\"Pat Payer\",\"phone\":\"+15555550100\"},\"metadata\":{\"invoiceId\":\"inv-1\"},\"payment_method_details\":{\"card\":{\"last4\":\"4242\"}}}}}";
			var minimized = PaymentWebhookPayloadMinimizer.Minimize(raw);
			var json = JObject.Parse(minimized);
			json["id"].Value<string>().Should().Be("evt_1");
			json["data"]["object"]["payment_intent"].Value<string>().Should().Be("pi_1");
			json["data"]["object"]["customer"].Value<string>().Should().Be("cus_1", "a bare customer id is a reconciliation key");
			json["data"]["object"]["metadata"]["invoiceId"].Value<string>().Should().Be("inv-1");
			minimized.Should().NotContain("payer@example.com").And.NotContain("Pat Payer").And.NotContain("5555550100").And.NotContain("4242");
			PaymentWebhookPayloadMinimizer.Minimize("{\"id\":\"evt_1\"}").Should().Be("{\"id\":\"evt_1\"}");
			PaymentWebhookPayloadMinimizer.Minimize("not json").Should().BeNull();
		}

		#endregion

		#region Invoicing stays whole

		[Test]
		public async Task Line_items_keep_their_ids_and_invoice_text_is_stored_whole_without_a_protection_seam()
		{
			var stored = new List<InvoiceLineItem> { new InvoiceLineItem { InvoiceLineItemId = "line-a", InvoiceId = "inv-1", DepartmentId = DeptId, Description = "Standby crew", Quantity = 1, UnitRate = 10, Amount = 10 }, new InvoiceLineItem { InvoiceLineItemId = "line-b", InvoiceId = "inv-1", DepartmentId = DeptId, Description = "old", Quantity = 1, UnitRate = 5, Amount = 5 } };
			var invoices = new Mock<IInvoiceRepository>();
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = DeptId, Status = (int)InvoiceStatus.Draft, ContactId = "c1", Currency = "USD" };
			invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-1", DeptId)).ReturnsAsync(invoice);
			invoices.Setup(r => r.SaveOrUpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((Invoice i, CancellationToken _, bool __) => i);
			var lines = new Mock<IInvoiceLineItemRepository>();
			lines.Setup(r => r.GetByInvoiceIdAsync("inv-1", DeptId)).ReturnsAsync(() => stored.ToList());
			lines.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoiceLineItem>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((InvoiceLineItem l, CancellationToken _, bool __) => { l.InvoiceLineItemId ??= Guid.NewGuid().ToString(); stored.RemoveAll(x => x.InvoiceLineItemId == l.InvoiceLineItemId); stored.Add(l); return l; });
			lines.Setup(r => r.DeleteAsync(It.IsAny<InvoiceLineItem>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvoiceLineItem l, CancellationToken _) => stored.RemoveAll(x => x.InvoiceLineItemId == l.InvoiceLineItemId) > 0);
			var payments = new Mock<IInvoicePaymentRepository>();
			payments.Setup(r => r.GetByInvoiceIdAsync("inv-1", DeptId)).ReturnsAsync(new List<InvoicePayment>());
			var identities = new Mock<IDepartmentBillingIdentityRepository>();
			DepartmentBillingIdentity storedIdentity = null;
			identities.Setup(r => r.GetByDepartmentIdAsync(DeptId)).ReturnsAsync(() => storedIdentity);
			identities.Setup(r => r.UpsertAsync(It.IsAny<DepartmentBillingIdentity>(), It.IsAny<CancellationToken>())).ReturnsAsync((DepartmentBillingIdentity i, CancellationToken _) => { storedIdentity = i; return i; });
			var protectedRead = new Mock<IProtectedReadService>(MockBehavior.Strict);
			var service = new InvoicingService(new Mock<ICustomerBillingProfileRepository>().Object, new Mock<IRateCardRepository>().Object, new Mock<IRateCardItemRepository>().Object, invoices.Object, lines.Object, payments.Object,
				new Mock<IInvoiceNumberSequenceRepository>().Object, identities.Object, new Mock<IContactsService>().Object, new Mock<ICallsService>().Object, new Mock<IUnitsService>().Object,
				new Mock<IDomainEventOutboxService>().Object, new Mock<IEventAggregator>().Object, new Mock<IPdfProvider>().Object, new Mock<IEmailService>().Object, new Mock<IDepartmentsService>().Object, new Mock<IAddressService>().Object, null,
				null, new Lazy<IProtectedReadService>(() => protectedRead.Object));

			// Edits line-a's text, keeps line-b, adds a new line: the customer reads every description on the invoice, so they are stored as typed.
			await service.SaveInvoiceLineItemsAsync("inv-1", DeptId, new List<InvoiceLineItem>
			{
				new InvoiceLineItem { InvoiceLineItemId = "line-a", Description = "Standby crew (day 2)", Quantity = 1, UnitRate = 10 },
				new InvoiceLineItem { InvoiceLineItemId = "line-b", Description = "Transport for J. Doe", Quantity = 2, UnitRate = 5 },
				new InvoiceLineItem { Description = "Mileage", Quantity = 10, UnitRate = 1 }
			}, "clerk", null, null);

			stored.Select(l => l.InvoiceLineItemId).Should().Contain(new[] { "line-a", "line-b" }, "existing lines keep their row keys so provenance links and audit history follow the row");
			stored.Should().HaveCount(3);
			stored.Select(l => l.Description).Should().BeEquivalentTo(new[] { "Standby crew (day 2)", "Transport for J. Doe", "Mileage" });
			stored.Should().OnlyContain(l => !l.IsProtected && l.ProtectedCatalogVersion == 0, "nothing on an invoice is enveloped");
			await FluentActions.Awaiting(() => service.SaveInvoiceLineItemsAsync("inv-1", DeptId, new List<InvoiceLineItem> { new InvoiceLineItem { Description = " ", Quantity = 1, UnitRate = 1 } }, "clerk", null, null)).Should().ThrowAsync<ArgumentException>("every line needs a description");

			// The department's registrations print on every invoice: saved and read back verbatim, never through the read seam.
			var identity = await service.SaveDepartmentBillingIdentityAsync(new DepartmentBillingIdentity { DepartmentId = DeptId, TaxRegistrationNumber = "12-3456789", SamUei = "ABC123DEF456", PayLinkExpiryDays = 30 }, "clerk", null, null);
			identity.IsProtected.Should().BeFalse();
			(await service.GetDepartmentBillingIdentityAsync(DeptId)).TaxRegistrationNumber.Should().Be("12-3456789");
			(await service.GetInvoiceByIdAsync("inv-1", DeptId)).LineItems.Select(l => l.Description).Should().Contain("Transport for J. Doe");
			protectedRead.VerifyNoOtherCalls();
		}

		#endregion

		#region Deployment notes seam and events

		[Test]
		public async Task Deployment_notes_stay_enveloped_while_attachments_and_time_reports_are_stored_whole_and_the_completion_triggers_publish()
		{
			var published = new List<DomainEventEnvelope>();
			var outbox = new Mock<IDomainEventOutboxService>();
			outbox.Setup(o => o.EnqueueAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, DomainEventEnvelope, CancellationToken>((_, __, e, ___) => published.Add(e)).ReturnsAsync(new DomainEventOutboxEntry());
			var write = new Mock<IProtectedWriteService>();
			var enveloped = new List<string>();
			write.Setup(w => w.PrepareRecordsEntityWriteAsync(DeptId, It.IsAny<Deployment>(), It.IsAny<Deployment>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<Deployment, string>, Action<Deployment, string>)>>(), It.IsAny<Action>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.Callback<int, Deployment, Deployment, string, IReadOnlyDictionary<string, (Func<Deployment, string>, Action<Deployment, string>)>, Action, string, string, bool, CancellationToken>((_, e, __, k, a, mark, ___, ____, _____, ______) => { enveloped.Add("deployment:" + k); foreach (var acc in a) if (!string.IsNullOrEmpty(acc.Value.Item1(e))) acc.Value.Item2(e, "rgdp:" + acc.Value.Item1(e)); mark(); })
				.ReturnsAsync(new ProtectedWriteResult { Success = true, Changed = true });
			var read = new Mock<IProtectedReadService>(MockBehavior.Strict);
			var readGrants = new List<string>();
			read.Setup(r => r.ResolveRecordsEntitiesForReadAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<(Deployment, string)>>(), It.IsAny<IReadOnlyDictionary<string, (Func<Deployment, string>, Action<Deployment, string>)>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback<int, IReadOnlyList<(Deployment, string)>, IReadOnlyDictionary<string, (Func<Deployment, string>, Action<Deployment, string>)>, string, string, CancellationToken>((_, rows, a, g, __, ___) => { readGrants.Add(g); foreach (var row in rows) foreach (var acc in a) if (acc.Value.Item1(row.Item1)?.StartsWith("rgdp:") == true) acc.Value.Item2(row.Item1, g == "grant-123" ? acc.Value.Item1(row.Item1).Substring(5) : ProtectedDataEnvelope.RedactionValue); })
				.ReturnsAsync(new ProtectedReadResult());
			var storedDeployments = new List<Deployment>();
			var deployments = new Mock<IDeploymentRepository>();
			// The fake repository hands out copies, as Dapper does: a read resolved in memory must never "decrypt" the stored row.
			deployments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<Deployment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((Deployment d, CancellationToken _, bool __) => { d.DeploymentId ??= "dep-1"; storedDeployments.RemoveAll(x => x.DeploymentId == d.DeploymentId); storedDeployments.Add(Resgrid.Framework.ObjectCopier.CloneJson(d)); return d; });
			deployments.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => { var d = storedDeployments.FirstOrDefault(x => x.DeploymentId == id); return d == null ? null : Resgrid.Framework.ObjectCopier.CloneJson(d); });
			var units = new Mock<IDeploymentUnitRepository>(); units.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync(new List<DeploymentUnit>());
			var personnel = new Mock<IDeploymentPersonnelRepository>(); personnel.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync(new List<DeploymentPersonnel>());
			var equipment = new Mock<IDeploymentEquipmentRepository>(); equipment.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync(new List<DeploymentEquipment>());
			var storedAttachments = new List<DeploymentAttachment>();
			var attachments = new Mock<IDeploymentAttachmentRepository>();
			attachments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((DeploymentAttachment a, CancellationToken _, bool __) => { if (a.DeploymentAttachmentId == 0) a.DeploymentAttachmentId = 41; storedAttachments.RemoveAll(x => x.DeploymentAttachmentId == a.DeploymentAttachmentId); storedAttachments.Add(a); return a; });
			var events = new Mock<IEventAggregator>();
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, Name = "Test", TimeZone = "UTC" });
			var grant = new Grant { GrantToken = "grant-123", UserId = "clerk" };
			var deploymentService = new DeploymentService(deployments.Object, units.Object, personnel.Object, equipment.Object, attachments.Object, departments.Object, new Mock<IUnitsService>().Object, new Mock<IUserProfileService>().Object,
				new Mock<IPersonnelRolesService>().Object, new Mock<ICertificationService>().Object, new Mock<IContactsService>().Object, new Mock<ICallsService>().Object, new Mock<IRecordDeploymentsService>().Object, outbox.Object, events.Object, new Mock<IPdfProvider>().Object, null,
				null, new Lazy<IProtectedWriteService>(() => write.Object), new Lazy<IProtectedReadService>(() => read.Object), grant);

			// Deployment notes are enveloped on save; a grant holder reads them back, everyone else sees REDACTED.
			var saved = await deploymentService.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = "Ridge Fire", Notes = "Contact Jane on site" }, "clerk", null, null);
			enveloped.Should().Contain("deployment:dep-1");
			storedDeployments.Single().Notes.Should().Be("rgdp:Contact Jane on site");
			storedDeployments.Single().IsProtected.Should().BeTrue();
			saved.Notes.Should().Be("Contact Jane on site", "the grant holder who just saved reads the value back");
			grant.GrantToken = null;
			(await deploymentService.GetDeploymentByIdAsync("dep-1", DeptId)).Notes.Should().Be(ProtectedDataEnvelope.RedactionValue);
			readGrants.Should().Contain("grant-123").And.Contain((string)null);
			grant.GrantToken = "grant-123";

			// An unrevealed edit posts REDACTED back: the seam receives the stored row as the sentinel source.
			var edit = await deploymentService.GetDeploymentByIdAsync("dep-1", DeptId);
			edit.Notes = ProtectedDataEnvelope.RedactionValue;
			await deploymentService.SaveDeploymentAsync(edit, "clerk", null, null);
			write.Verify(w => w.PrepareRecordsEntityWriteAsync(DeptId, It.IsAny<Deployment>(), It.Is<Deployment>(x => x != null && x.Notes == "rgdp:Contact Jane on site"), "dep-1", It.IsAny<IReadOnlyDictionary<string, (Func<Deployment, string>, Action<Deployment, string>)>>(), It.IsAny<Action>(), null, null, true, It.IsAny<CancellationToken>()), Times.Once);

			// Attachment added: trigger 187 carries the name as typed; attachments go to customers in the invoice packet.
			await deploymentService.SaveAttachmentAsync(new DeploymentAttachment { DeploymentId = "dep-1", DepartmentId = DeptId, AttachmentType = (int)DeploymentAttachmentTypes.SignedServiceRequest, Name = "Signed request", FileName = "signed.pdf", FileType = "application/pdf", Data = new byte[] { 1 } }, "clerk", null, null);
			var attachmentEvent = published.Single(e => e.Trigger == WorkflowTriggerEventType.DeploymentAttachmentAdded);
			var attachmentPayload = JObject.FromObject(attachmentEvent.Payload);
			attachmentPayload["AttachmentId"].Value<int>().Should().Be(41);
			attachmentPayload["AttachmentType"].Value<int>().Should().Be((int)DeploymentAttachmentTypes.SignedServiceRequest);
			attachmentPayload["AttachmentName"].Value<string>().Should().Be("Signed request");
			storedAttachments.Single().Name.Should().Be("Signed request");
			storedAttachments.Single().IsProtected.Should().BeFalse();

			// Time reports: created / voided publish, the void reason is appended to the note as typed, entries keep their ids.
			var storedReports = new List<DeploymentTimeReport>();
			var reports = new Mock<IDeploymentTimeReportRepository>();
			reports.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentTimeReport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((DeploymentTimeReport t, CancellationToken _, bool __) => { t.DeploymentTimeReportId ??= "rep-1"; storedReports.RemoveAll(x => x.DeploymentTimeReportId == t.DeploymentTimeReportId); storedReports.Add(t); return t; });
			reports.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => storedReports.FirstOrDefault(t => t.DeploymentTimeReportId == id));
			reports.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync(() => storedReports.ToList());
			var storedEntries = new List<DeploymentTimeEntry>();
			var entries = new Mock<IDeploymentTimeEntryRepository>();
			entries.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentTimeEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((DeploymentTimeEntry e, CancellationToken _, bool __) => { e.DeploymentTimeEntryId ??= "ent-" + (storedEntries.Count + 1); storedEntries.RemoveAll(x => x.DeploymentTimeEntryId == e.DeploymentTimeEntryId); storedEntries.Add(e); return e; });
			entries.Setup(r => r.GetByReportAsync(It.IsAny<string>())).ReturnsAsync((string id) => storedEntries.Where(e => e.DeploymentTimeReportId == id).ToList());
			entries.Setup(r => r.DeleteAsync(It.IsAny<DeploymentTimeEntry>(), It.IsAny<CancellationToken>())).ReturnsAsync((DeploymentTimeEntry e, CancellationToken _) => storedEntries.RemoveAll(x => x.DeploymentTimeEntryId == e.DeploymentTimeEntryId) > 0);
			var sequence = new Mock<ITimeReportNumberSequenceRepository>(); sequence.Setup(s => s.GetNextNumberAsync(DeptId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
			var expenses = new Mock<IDeploymentExpenseRepository>();
			var timeTracking = new TimeTrackingService(deployments.Object, personnel.Object, units.Object, equipment.Object, reports.Object, entries.Object, expenses.Object, attachments.Object, sequence.Object, deploymentService, departments.Object, new Mock<IUserProfileService>().Object, new Mock<IUnitsService>().Object, events.Object, new Mock<IPdfProvider>().Object, null);
			var roster = new List<DeploymentPersonnel> { new DeploymentPersonnel { DeploymentPersonnelId = "per-1", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "alice" } };
			personnel.Setup(r => r.GetByDeploymentAsync("dep-1")).ReturnsAsync(roster);

			var report = await timeTracking.CreateTimeReportAsync("dep-1", DeptId, new DateTime(2026, 9, 21), "alice", null, null);
			published.Should().Contain(e => e.Trigger == WorkflowTriggerEventType.TimeReportCreated && e.CorrelationId == report.DeploymentTimeReportId);
			JObject.FromObject(published.Single(e => e.Trigger == WorkflowTriggerEventType.TimeReportCreated).Payload)["ReportStatus"].Value<int>().Should().Be((int)DeploymentTimeReportStatuses.Draft);

			var firstEntryId = report.Entries.Single().DeploymentTimeEntryId;
			var batch = await timeTracking.SaveTimeEntriesAsync(report.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry>
			{
				new DeploymentTimeEntry { DeploymentTimeEntryId = firstEntryId, DeploymentPersonnelId = "per-1", StartTime = new DateTime(2026, 9, 21, 8, 0, 0), EndTime = new DateTime(2026, 9, 21, 12, 0, 0), Notes = "Relieved by J. Doe" },
				new DeploymentTimeEntry { DeploymentPersonnelId = "per-1", StartTime = new DateTime(2026, 9, 21, 13, 0, 0), EndTime = new DateTime(2026, 9, 21, 17, 0, 0), Notes = "Relief crew" }
			}, "alice", null, null);
			batch.Validation.IsValid.Should().BeTrue();
			storedEntries.Select(e => e.DeploymentTimeEntryId).Should().Contain(firstEntryId, "an existing entry is updated in place, never re-inserted under a new key");
			storedEntries.Select(e => e.Notes).Should().BeEquivalentTo(new[] { "Relieved by J. Doe", "Relief crew" }, "the customer signs the DTR; its notes are stored as typed");
			storedReports.Single().IsProtected.Should().BeFalse("nothing on a DTR is enveloped");
			enveloped.Should().OnlyContain(k => k.StartsWith("deployment:"), "the deployment wrapper's internal notes are the only seam left in Phase C");

			storedReports.Single().Notes = "Crew note";
			var voided = await timeTracking.VoidTimeReportAsync(report.DeploymentTimeReportId, DeptId, "duplicate", "chief", null, null);
			storedReports.Single().Notes.Should().Be("Crew note\nVoid: duplicate", "the reason is appended to the note as typed");
			published.Should().Contain(e => e.Trigger == WorkflowTriggerEventType.TimeReportVoided);
			JObject.FromObject(published.Single(e => e.Trigger == WorkflowTriggerEventType.TimeReportVoided).Payload)["ReportStatus"].Value<int>().Should().Be((int)DeploymentTimeReportStatuses.Void);
			voided.Status.Should().Be((int)DeploymentTimeReportStatuses.Void);
		}

		#endregion

		#region Workflow catalog coverage

		[Test]
		public void Every_completion_trigger_has_catalog_variables_sample_data_and_a_baseline_entry()
		{
			var triggers = new[]
			{
				WorkflowTriggerEventType.UnitCertificationAdded, WorkflowTriggerEventType.UnitCertificationStatusChanged, WorkflowTriggerEventType.UnitCertificationRemoved,
				WorkflowTriggerEventType.CertificationRemoved, WorkflowTriggerEventType.CertificationCreditAdded,
				WorkflowTriggerEventType.TimeReportCreated, WorkflowTriggerEventType.TimeReportVoided, WorkflowTriggerEventType.DeploymentAttachmentAdded
			};
			foreach (var trigger in triggers)
			{
				var names = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger).Select(v => v.Name).ToList();
				names.Should().Contain(n => n.StartsWith("certification.") || n.StartsWith("unit_certification.") || n.StartsWith("deployment."), trigger.ToString());
				WorkflowSampleDataGenerator.GenerateSampleData(trigger).Should().NotBeNull(trigger.ToString());
			}
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CertificationCreditAdded).Select(v => v.Name).Should().Contain(new[] { "credit.hours", "credit.category" });
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.UnitCertificationStatusChanged).Select(v => v.Name).Should().Contain("unit_certification.old_status");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.DeploymentAttachmentAdded).Select(v => v.Name).Should().Contain(new[] { "deployment.attachment_id", "deployment.attachment_name" });
			CertificationWorkflowTriggers.Triggers.Should().Contain(new[] { 180, 181, 182, 183, 184 });
			DeploymentWorkflowPayload.Triggers.Should().Contain(new[] { 185, 186, 187 });
			DeploymentWorkflowPayload.Reserved.Should().BeEmpty("C-M2 published 74-80 under ContractorWorkflowPayload; nothing stays hidden from the picker");
			ContractorWorkflowPayload.BidTriggers.Concat(ContractorWorkflowPayload.ContractTriggers).Should().BeEquivalentTo(new[] { 74, 75, 76, 77, 78, 79, 80 });
		}

		#endregion
	}
}
