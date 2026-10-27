using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;
using Resgrid.Services.IncidentLogistics;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Incident Back Office (large-incident-logistics-finance-plan.md rev 4.1): catalog parity between the schema, the desks, ADP and
	/// cleanup; the fixed lifecycles; workflow payload safety; and the table rules that carry the module's invariants.
	/// </summary>
	[TestFixture]
	public sealed class IncidentBackOfficeTests
	{
		private const int DepartmentId = 77;
		private const string ActivationId = "aaaaaaaa-0000-0000-0000-000000000001";

		private static IncidentActor Actor(string userId = "user-1", bool admin = false) => new IncidentActor { DepartmentId = DepartmentId, UserId = userId, IsDepartmentAdmin = admin };

		#region Catalog parity

		[Test]
		public void Every_table_has_a_desk_descriptor_whose_fields_are_entity_properties()
		{
			var tables = IncidentBackOfficeTables.All.Select(t => t.Table).ToList();
			IncidentLedgerCatalog.All.Values.Select(d => d.Table).Should().BeEquivalentTo(tables);
			foreach (var descriptor in IncidentLedgerCatalog.All.Values)
			{
				var properties = descriptor.EntityType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
				descriptor.Fields.Select(f => f.Name).Should().OnlyContain(f => properties.Contains(f), descriptor.Key);
				descriptor.Fields.Should().NotContain(f => f.Name == "DepartmentId" || f.Name == "IsDeleted" || f.Name == "RowVersion", descriptor.Key);
				if (descriptor.StatusField != null)
					properties.Should().Contain(descriptor.StatusField, descriptor.Key);
			}
		}

		[Test]
		public void Every_protected_column_on_a_desk_is_protected_and_never_a_list_column()
		{
			foreach (var (table, column, _, _, _) in IncidentBackOfficeProtectedFields.All())
			{
				var descriptor = IncidentLedgerCatalog.All.Values.Single(d => d.Table == table);
				// A catalogued column is either kept off the desk (system-written JSON) or shown only as a protected field.
				var field = descriptor.Fields.SingleOrDefault(f => f.Name == column);
				if (field == null)
					continue;
				field.Protected.Should().BeTrue($"{table}.{column} is ADP-catalogued");
				field.InList.Should().BeFalse($"{table}.{column} must never be a list column");
			}
		}

		[Test]
		public void Adp_catalog_and_table_bindings_cover_every_protected_column()
		{
			var catalog = new ProtectedFieldCatalog();
			foreach (var (table, column, classification, _, version) in IncidentBackOfficeProtectedFields.All())
			{
				var entry = catalog.GetForTable(table).SingleOrDefault(e => string.Equals(e.ColumnName, column, StringComparison.Ordinal));
				entry.Should().NotBeNull($"{table}.{column}");
				entry.AddedInCatalogVersion.Should().Be(version);
				entry.Classification.ToString().Should().Be(classification);
				AdpTableBindings.V1.Should().Contain(b => b.TableName == table && b.Columns.Any(c => c.ColumnName == column), $"{table}.{column}");
			}
			catalog.Version.Should().BeGreaterThanOrEqualTo(IncidentBackOfficeProtectedFields.BusinessCatalogVersion);
		}

		[Test]
		public void Department_deletion_purges_every_module_table_and_the_billing_account()
		{
			IncidentBackOfficeDepartmentCleanup.Tables.Should().BeEquivalentTo(IncidentBackOfficeTables.All.Select(t => t.Table).Append(IncidentBackOfficeTables.BillingAccounts));
		}

		[Test]
		public void Navigation_links_only_desks_that_exist()
		{
			var controllers = typeof(Resgrid.Web.Areas.User.Controllers.IncidentBackOfficeControllerBase).Assembly.GetTypes()
				.Where(t => !t.IsAbstract && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t)).Select(t => t.Name.Replace("Controller", string.Empty)).ToHashSet();
			foreach (var item in IncidentBackOfficeNavigation.Sections.SelectMany(s => s.Items))
				controllers.Should().Contain(item.Controller);
			foreach (var descriptor in IncidentLedgerCatalog.All.Values)
				controllers.Should().Contain(descriptor.Controller, descriptor.Key);
		}

		#endregion

		#region Lifecycles

		[Test]
		public void Every_status_field_has_a_lifecycle_and_every_lifecycle_moves_between_defined_statuses()
		{
			var builder = new ContainerBuilder();
			IncidentLifecycles.Register(builder);
			using var container = builder.Build();
			foreach (var descriptor in IncidentLedgerCatalog.All.Values.Where(d => d.StatusField != null))
			{
				var lifecycleType = typeof(IIncidentLifecycle<>).MakeGenericType(descriptor.EntityType);
				var lifecycle = container.ResolveOptional(lifecycleType);
				lifecycle.Should().NotBeNull(descriptor.Key);
				var statusEnum = (Type)lifecycleType.GetProperty("StatusEnum").GetValue(lifecycle);
				var next = lifecycleType.GetMethod("NextStatuses");
				var terminal = lifecycleType.GetMethod("IsTerminal");
				foreach (var status in Enum.GetValues(statusEnum).Cast<object>().Select(Convert.ToInt32))
				{
					var targets = (IReadOnlyList<int>)next.Invoke(lifecycle, new object[] { status });
					targets.Should().OnlyContain(t => Enum.IsDefined(statusEnum, t), descriptor.Key);
					targets.Should().NotContain(status, descriptor.Key);
					if ((bool)terminal.Invoke(lifecycle, new object[] { status }))
						targets.Should().BeEmpty($"{descriptor.Key} {status} is terminal");
				}
			}
		}

		#endregion

		#region Workflow payload

		[Test]
		public void Every_incident_trigger_is_catalogued_with_its_scope()
		{
			var incident = Enum.GetValues<WorkflowTriggerEventType>().Where(t => (int)t is >= 116 and <= 148 or >= 192 and <= 207).ToList();
			IncidentBackOfficeWorkflowPayload.Triggers.Should().BeEquivalentTo(incident.Select(t => (int)t));
			IncidentBackOfficeWorkflowPayload.Business.Should().OnlyContain(t => IncidentBackOfficeWorkflowPayload.IsIncident(t));
			IncidentBackOfficeWorkflowPayload.Coordination.Should().OnlyContain(t => IncidentBackOfficeWorkflowPayload.IsIncident(t));
			IncidentBackOfficeWorkflowPayload.DepartmentScoped.Should().OnlyContain(t => IncidentBackOfficeWorkflowPayload.IsIncident(t));
			foreach (var trigger in incident)
			{
				var variables = IncidentBackOfficeWorkflowPayload.For(trigger).ToList();
				variables.Should().NotBeEmpty(trigger.ToString());
				variables.Select(v => v.Variable).Should().OnlyHaveUniqueItems(trigger.ToString());
				WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger).Select(v => v.Name).Should().Contain(variables.Select(v => v.Variable), trigger.ToString());
			}
		}

		[Test]
		public void No_trigger_variable_names_a_protected_column()
		{
			var protectedColumns = IncidentBackOfficeProtectedFields.All().Select(f => f.Column.ToLowerInvariant()).ToHashSet();
			foreach (var trigger in Enum.GetValues<WorkflowTriggerEventType>().Where(t => IncidentBackOfficeWorkflowPayload.IsIncident((int)t)))
				foreach (var (variable, _, _) in IncidentBackOfficeWorkflowPayload.For(trigger))
				{
					var leaf = variable.Split('.').Last().Replace("_", string.Empty);
					protectedColumns.Should().NotContain(leaf, $"{trigger} {variable}");
				}
		}

		[Test]
		public void Sample_data_nests_every_catalogued_variable()
		{
			foreach (var trigger in Enum.GetValues<WorkflowTriggerEventType>().Where(t => IncidentBackOfficeWorkflowPayload.IsIncident((int)t)))
			{
				var sample = (Scriban.Runtime.ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
				foreach (var (variable, _, _) in IncidentBackOfficeWorkflowPayload.For(trigger))
				{
					object node = sample;
					foreach (var part in variable.Split('.'))
					{
						node.Should().BeOfType<Scriban.Runtime.ScriptObject>($"{trigger} {variable}");
						((Scriban.Runtime.ScriptObject)node).ContainsKey(part).Should().BeTrue($"{trigger} {variable}");
						node = ((Scriban.Runtime.ScriptObject)node)[part];
					}
				}
			}
		}

		#endregion

		#region Rules

		private static IncidentLedgerChange<T> Change<T>(T entity, T existing = null, int? from = null, int? to = null, string reason = null, IncidentActor actor = null) where T : class, IIncidentLedgerEntity =>
			new IncidentLedgerChange<T>
			{
				Actor = actor ?? Actor(),
				Entity = entity,
				Existing = existing,
				OldStatus = from,
				NewStatus = to,
				Reason = reason,
				IsTransition = to.HasValue,
				Activation = new IncidentActivation { IncidentActivationId = ActivationId, DepartmentId = DepartmentId, ShortCode = "RIDGE", Name = "Ridge Fire" }
			};

		[Test]
		public async Task Numbering_takes_the_next_number_of_the_activation_and_keeps_a_typed_one()
		{
			var rows = new Mock<IIncidentLedgerRepository<IncidentResourceRequest>>();
			rows.Setup(r => r.CountAsync(DepartmentId, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(6);
			var activations = new Mock<IIncidentLedgerRepository<IncidentActivation>>();
			activations.Setup(r => r.LockForUpdateAsync(DepartmentId, ActivationId, It.IsAny<CancellationToken>())).ReturnsAsync(new IncidentActivation { IncidentActivationId = ActivationId, ShortCode = "RIDGE" });
			var rule = new IncidentNumberingRule<IncidentResourceRequest>(rows.Object, activations.Object, e => e.RequestNumber, (e, v) => e.RequestNumber = v, "R");

			var request = new IncidentResourceRequest { IncidentActivationId = ActivationId };
			await rule.BeforeSaveAsync(Change(request), CancellationToken.None);
			request.RequestNumber.Should().Be("RIDGE-R0007");

			var typed = new IncidentResourceRequest { IncidentActivationId = ActivationId, RequestNumber = "CA-XYZ-000123" };
			await rule.BeforeSaveAsync(Change(typed), CancellationToken.None);
			typed.RequestNumber.Should().Be("CA-XYZ-000123");

			var edited = new IncidentResourceRequest { IncidentActivationId = ActivationId, RequestNumber = "CHANGED" };
			await rule.BeforeSaveAsync(Change(edited, new IncidentResourceRequest { RequestNumber = "RIDGE-R0002" }), CancellationToken.None);
			edited.RequestNumber.Should().Be("RIDGE-R0002", "a number never changes after the row exists");
		}

		private static (IncidentLodgingNightRule Rule, Mock<IIncidentLedgerRepository<IncidentLodgingNightInventory>> Nights) LodgingRule(IEnumerable<IncidentLodgingNightInventory> taken, IncidentLodgingBed bed = null)
		{
			var nights = new Mock<IIncidentLedgerRepository<IncidentLodgingNightInventory>>();
			nights.Setup(r => r.ListAsync(DepartmentId, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(taken);
			nights.Setup(r => r.InsertNewAsync(It.IsAny<IncidentLodgingNightInventory>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IncidentLodgingNightInventory n, string _, CancellationToken __) => n);
			var beds = new Mock<IIncidentLedgerRepository<IncidentLodgingBed>>();
			beds.Setup(r => r.LockForUpdateAsync(DepartmentId, "bed-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(bed ?? new IncidentLodgingBed { IncidentLodgingBedId = "bed-1", IncidentActivationId = ActivationId, IncidentLodgingPropertyId = "prop-1" });
			return (new IncidentLodgingNightRule(nights.Object, beds.Object), nights);
		}

		private static IncidentLodgingAssignment Stay(string id = "stay-1") => new IncidentLodgingAssignment
		{
			IncidentLodgingAssignmentId = id, IncidentActivationId = ActivationId, IncidentLodgingBedId = "bed-1",
			FirstNight = new DateTime(2026, 11, 1), LastNight = new DateTime(2026, 11, 3), Status = (int)IncidentLodgingAssignmentStatuses.Held
		};

		[Test]
		public async Task A_bed_night_held_by_another_assignment_is_refused()
		{
			var (rule, _) = LodgingRule(new[] { new IncidentLodgingNightInventory { IncidentLodgingBedId = "bed-1", Night = new DateTime(2026, 11, 2), IncidentLodgingAssignmentId = "other" } });
			var act = () => rule.BeforeSaveAsync(Change(Stay()), CancellationToken.None);
			(await act.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("BedOccupied");
		}

		[Test]
		public async Task An_assignment_needing_an_accessible_bed_cannot_take_another()
		{
			var (rule, _) = LodgingRule(Array.Empty<IncidentLodgingNightInventory>());
			var stay = Stay();
			stay.IsAccessible = true;
			var act = () => rule.BeforeSaveAsync(Change(stay), CancellationToken.None);
			(await act.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("BedNotAccessible");
		}

		[Test]
		public async Task A_held_assignment_writes_one_row_per_night_and_a_cancellation_frees_them()
		{
			var (rule, nights) = LodgingRule(Array.Empty<IncidentLodgingNightInventory>());
			var stay = Stay();
			await rule.BeforeSaveAsync(Change(stay), CancellationToken.None);
			stay.IncidentLodgingPropertyId.Should().Be("prop-1");
			await rule.AfterSaveAsync(Change(stay), CancellationToken.None);
			nights.Verify(r => r.InsertNewAsync(It.IsAny<IncidentLodgingNightInventory>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

			var cancelled = Stay();
			cancelled.Status = (int)IncidentLodgingAssignmentStatuses.Cancelled;
			await rule.AfterSaveAsync(Change(cancelled, Stay(), (int)IncidentLodgingAssignmentStatuses.Held, (int)IncidentLodgingAssignmentStatuses.Cancelled), CancellationToken.None);
			nights.Verify(r => r.PurgeWhereAsync(DepartmentId, "IncidentLodgingAssignmentId", "stay-1", null, null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
		}

		private static IncidentVendorCapacityRule CapacityRule(decimal max, IEnumerable<IncidentVendorCapacityCommitment> others, out Mock<IIncidentEventPublisher> publisher)
		{
			var commitments = new Mock<IIncidentLedgerRepository<IncidentVendorCapacityCommitment>>();
			commitments.Setup(r => r.ListAsync(DepartmentId, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(others);
			var capabilities = new Mock<IIncidentLedgerRepository<VendorCapability>>();
			capabilities.Setup(r => r.LockForUpdateAsync(DepartmentId, "cap-1", It.IsAny<CancellationToken>())).ReturnsAsync(new VendorCapability { VendorCapabilityId = "cap-1", VendorId = "vendor-1", MaxCapacity = max, Unit = "meals", Name = "Catering" });
			var engagements = new Mock<IIncidentLedgerRepository<IncidentVendorEngagement>>();
			engagements.Setup(r => r.GetForDepartmentAsync(DepartmentId, "eng-1", false, It.IsAny<CancellationToken>())).ReturnsAsync(new IncidentVendorEngagement { IncidentVendorEngagementId = "eng-1", IncidentActivationId = ActivationId, VendorId = "vendor-1" });
			var settings = new Mock<IIncidentBackOfficeSettingsService>();
			settings.Setup(s => s.GetAsync<IncidentVendorPolicy>(DepartmentId, false)).ReturnsAsync(new IncidentVendorPolicy());
			publisher = new Mock<IIncidentEventPublisher>();
			var lookups = new Mock<IIncidentLookupService>();
			lookups.Setup(l => l.GetLabelsAsync(It.IsAny<IncidentActor>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, string>());
			return new IncidentVendorCapacityRule(commitments.Object, capabilities.Object, engagements.Object, settings.Object, publisher.Object, lookups.Object);
		}

		private static IncidentVendorCapacityCommitment Hold(decimal quantity, bool overrideCapacity = false) => new IncidentVendorCapacityCommitment
		{
			IncidentVendorCapacityCommitmentId = "hold-new", IncidentActivationId = ActivationId, IncidentVendorEngagementId = "eng-1", VendorCapabilityId = "cap-1",
			BucketStart = new DateTime(2026, 11, 1, 6, 0, 0), BucketEnd = new DateTime(2026, 11, 1, 18, 0, 0), Quantity = quantity, Status = (int)IncidentHoldStatuses.Active,
			Kind = (int)IncidentCommitmentKinds.Commitment, IsOverride = overrideCapacity, OverrideReason = overrideCapacity ? "Mutual aid surge" : null
		};

		[Test]
		public async Task Vendor_capacity_is_shared_across_incidents_and_an_override_needs_an_administrator()
		{
			var other = new IncidentVendorCapacityCommitment
			{
				IncidentVendorCapacityCommitmentId = "hold-other", IncidentActivationId = "another-incident", VendorCapabilityId = "cap-1", Quantity = 80,
				BucketStart = new DateTime(2026, 11, 1, 0, 0, 0), BucketEnd = new DateTime(2026, 11, 2, 0, 0, 0), Status = (int)IncidentHoldStatuses.Active
			};
			var rule = CapacityRule(100, new[] { other }, out var publisher);

			await rule.BeforeSaveAsync(Change(Hold(20)), CancellationToken.None);
			var act = () => rule.BeforeSaveAsync(Change(Hold(21)), CancellationToken.None);
			(await act.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("VendorCapacityExceeded");

			var notAdmin = () => rule.BeforeSaveAsync(Change(Hold(21, true)), CancellationToken.None);
			await notAdmin.Should().ThrowAsync<IncidentRuleException>();

			var overridden = Hold(21, true);
			var change = Change(overridden, actor: Actor(admin: true));
			await rule.BeforeSaveAsync(change, CancellationToken.None);
			overridden.ApprovedByUserId.Should().Be("user-1");
			await rule.AfterSaveAsync(change, CancellationToken.None);
			publisher.Verify(p => p.PublishAsync(DepartmentId, It.IsAny<IncidentActivation>(), WorkflowTriggerEventType.IncidentVendorCapacityConflict,
				It.Is<IDictionary<string, object>>(v => (bool)v["was_override"] && (int)v["conflicting_incidents_count"] == 1), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		private static Mock<IIncidentBackOfficeSettingsService> Finance(bool singlePerson = false)
		{
			var settings = new Mock<IIncidentBackOfficeSettingsService>();
			settings.Setup(s => s.GetAsync<IncidentFinancePolicy>(DepartmentId, false)).ReturnsAsync(new IncidentFinancePolicy
			{
				AllowSinglePersonOperation = singlePerson,
				SinglePersonAcknowledgedByUserId = singlePerson ? "admin-1" : null
			});
			settings.Setup(s => s.GetAsync<IncidentAssignmentRulePolicy>(DepartmentId, false)).ReturnsAsync(new IncidentAssignmentRulePolicy());
			return settings;
		}

		[Test]
		public async Task The_requester_cannot_approve_unless_single_person_operation_is_acknowledged()
		{
			var requisitions = new Mock<IIncidentLedgerRepository<PurchaseRequisition>>();
			var delegations = new Mock<IIncidentLedgerRepository<IncidentSpendingDelegation>>();
			delegations.Setup(r => r.ListAsync(DepartmentId, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IncidentSpendingDelegation>());
			var requisition = new PurchaseRequisition { IncidentActivationId = ActivationId, RequestedByUserId = "user-1", EstimatedAmount = 500 };

			var strict = new IncidentProcurementRule(requisitions.Object, delegations.Object, Finance().Object);
			var act = () => strict.BeforeSaveAsync(Change(requisition, requisition, (int)IncidentRequisitionStatuses.Submitted, (int)IncidentRequisitionStatuses.Approved), CancellationToken.None);
			(await act.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("SeparationOfDuties");

			var relaxed = new IncidentProcurementRule(requisitions.Object, delegations.Object, Finance(true).Object);
			await relaxed.BeforeSaveAsync(Change(requisition, requisition, (int)IncidentRequisitionStatuses.Submitted, (int)IncidentRequisitionStatuses.Approved), CancellationToken.None);
			requisition.ApprovedByUserId.Should().Be("user-1");
			requisition.ApprovedOn.Should().NotBeNull();
		}

		[Test]
		public async Task Approval_above_a_recorded_delegation_is_refused()
		{
			var requisitions = new Mock<IIncidentLedgerRepository<PurchaseRequisition>>();
			var delegations = new Mock<IIncidentLedgerRepository<IncidentSpendingDelegation>>();
			delegations.Setup(r => r.ListAsync(DepartmentId, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[]
			{
				new IncidentSpendingDelegation { UserId = "approver", ThresholdAmount = 1000, EffectiveOn = DateTime.UtcNow.AddDays(-1), Action = (int)IncidentSpendingActions.Approve }
			});
			var rule = new IncidentProcurementRule(requisitions.Object, delegations.Object, Finance().Object);
			var requisition = new PurchaseRequisition { IncidentActivationId = ActivationId, RequestedByUserId = "requester", EstimatedAmount = 2500 };
			var act = () => rule.BeforeSaveAsync(Change(requisition, requisition, (int)IncidentRequisitionStatuses.Submitted, (int)IncidentRequisitionStatuses.Approved, actor: Actor("approver")), CancellationToken.None);
			(await act.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("ApprovalAboveDelegation");
		}

		[Test]
		public async Task Certified_time_cannot_be_edited_and_the_certifier_cannot_approve()
		{
			var rule = new IncidentTimeRecordRule(Finance().Object, Mock.Of<IIncidentEventPublisher>(), Mock.Of<IIncidentLookupService>());
			var stored = new IncidentTimeRecord { IncidentActivationId = ActivationId, StartOn = new DateTime(2026, 11, 1, 6, 0, 0), EndOn = new DateTime(2026, 11, 1, 18, 0, 0), Status = (int)IncidentTimeStatuses.Certified, CertifiedByUserId = "user-1" };
			var edit = () => rule.BeforeSaveAsync(Change(stored.CloneJson(), stored), CancellationToken.None);
			(await edit.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("TimeRecordLocked");

			var approve = () => rule.BeforeSaveAsync(Change(stored.CloneJson(), stored, (int)IncidentTimeStatuses.Certified, (int)IncidentTimeStatuses.Approved), CancellationToken.None);
			(await approve.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("SeparationOfDuties");

			var other = stored.CloneJson();
			await rule.BeforeSaveAsync(Change(other, stored, (int)IncidentTimeStatuses.Certified, (int)IncidentTimeStatuses.Approved, actor: Actor("approver")), CancellationToken.None);
			other.ApprovedByUserId.Should().Be("approver");
		}

		[Test]
		public async Task A_shift_over_the_work_rest_limit_is_recorded_and_raises_the_trigger()
		{
			var publisher = new Mock<IIncidentEventPublisher>();
			var rule = new IncidentTimeRecordRule(Finance().Object, publisher.Object, Mock.Of<IIncidentLookupService>());
			var record = new IncidentTimeRecord { IncidentTimeRecordId = "t1", IncidentActivationId = ActivationId, StartOn = new DateTime(2026, 11, 1, 6, 0, 0), EndOn = new DateTime(2026, 11, 2, 0, 0, 0) };
			await rule.BeforeSaveAsync(Change(record), CancellationToken.None);
			await rule.AfterSaveAsync(Change(record), CancellationToken.None);
			publisher.Verify(p => p.PublishAsync(DepartmentId, It.IsAny<IncidentActivation>(), WorkflowTriggerEventType.IncidentWorkRestViolation,
				It.Is<IDictionary<string, object>>(v => (decimal)v["hours.worked"] == 18m), "IncidentTimeRecords", "t1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_budget_transfer_moves_authority_between_lines_and_the_budget_follows()
		{
			var lines = new Dictionary<string, IncidentBudgetLine>
			{
				["from"] = new IncidentBudgetLine { IncidentBudgetLineId = "from", IncidentBudgetId = "b1", IncidentActivationId = ActivationId, Name = "Lodging", RevisedAmount = 5000 },
				["to"] = new IncidentBudgetLine { IncidentBudgetLineId = "to", IncidentBudgetId = "b1", IncidentActivationId = ActivationId, Name = "Feeding", RevisedAmount = 1000 }
			};
			var lineRepo = new Mock<IIncidentLedgerRepository<IncidentBudgetLine>>();
			lineRepo.Setup(r => r.LockForUpdateAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((int _, string id, CancellationToken __) => lines[id]);
			lineRepo.Setup(r => r.SumAsync(DepartmentId, "RevisedAmount", It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => lines.Values.Sum(l => l.RevisedAmount));
			var budgets = new Mock<IIncidentLedgerRepository<IncidentBudget>>();
			var budget = new IncidentBudget { IncidentBudgetId = "b1", IncidentActivationId = ActivationId, RevisedAmount = 6000 };
			budgets.Setup(r => r.LockForUpdateAsync(DepartmentId, "b1", It.IsAny<CancellationToken>())).ReturnsAsync(budget);
			var rule = new IncidentFundingAuthorizationRule(lineRepo.Object, budgets.Object);

			var transfer = new IncidentFundingAuthorization { IncidentActivationId = ActivationId, Kind = (int)IncidentFundingAuthorizationKinds.Transfer, IncidentBudgetLineId = "to", FromBudgetLineId = "from", Amount = 1500 };
			await rule.BeforeSaveAsync(Change(transfer), CancellationToken.None);
			await rule.AfterSaveAsync(Change(transfer), CancellationToken.None);
			lines["from"].RevisedAmount.Should().Be(3500);
			lines["to"].RevisedAmount.Should().Be(2500);
			budget.RevisedAmount.Should().Be(6000);

			var edit = () => rule.BeforeSaveAsync(Change(transfer, transfer), CancellationToken.None);
			(await edit.Should().ThrowAsync<IncidentRuleException>()).Which.MessageKey.Should().Be("AuthorizationAppendOnly");
		}

		[Test]
		public async Task The_alert_latch_fires_once_on_each_edge()
		{
			var rows = new List<IncidentDataFreshness>();
			var repo = new Mock<IIncidentLedgerRepository<IncidentDataFreshness>>();
			repo.Setup(r => r.ListAsync(DepartmentId, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => rows.ToList());
			repo.Setup(r => r.InsertNewAsync(It.IsAny<IncidentDataFreshness>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IncidentDataFreshness f, string _, CancellationToken __) => { rows.Add(f); return f; });
			repo.Setup(r => r.UpdateVersionedAsync(It.IsAny<IncidentDataFreshness>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IncidentDataFreshness f, int _, string __, CancellationToken ___) => f);
			var latch = new IncidentAlertLatch(repo.Object, Mock.Of<ICacheProvider>());
			var key = IncidentAlertLatch.Key(WorkflowTriggerEventType.IncidentWaterBelowThreshold, "plan-1");

			(await latch.SetAsync(DepartmentId, ActivationId, key, false)).Should().BeNull("nothing started");
			(await latch.SetAsync(DepartmentId, ActivationId, key, true)).Should().BeTrue();
			(await latch.SetAsync(DepartmentId, ActivationId, key, true)).Should().BeNull("still active");
			(await latch.SetAsync(DepartmentId, ActivationId, key, false)).Should().BeFalse("recovered");
			(await latch.SetAsync(DepartmentId, ActivationId, key, false)).Should().BeNull();
			key.Length.Should().BeLessThanOrEqualTo(64);
		}

		#endregion

		#region Desk forms

		[Test]
		public void Policy_form_binds_numbers_lists_and_the_routing_map_and_reports_bad_values()
		{
			var policy = new IncidentFinancePolicy();
			var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
			{
				["AllowSinglePersonOperation"] = "true",
				["IntakeAllowedDomains"] = "county.gov, state.gov; county.gov",
				["BudgetThresholdPercent"] = "75.5",
				["IntakeBacklogCount"] = "-3",
				["SinglePersonAcknowledgedByUserId"] = "attacker"
			});
			var errors = IncidentPolicyForm.Bind(policy, form);
			policy.AllowSinglePersonOperation.Should().BeTrue();
			policy.IntakeAllowedDomains.Should().Equal("county.gov", "state.gov");
			policy.BudgetThresholdPercent.Should().Be(75.5m);
			policy.SinglePersonAcknowledgedByUserId.Should().BeNull("acknowledgment stamps are never posted");
			errors.Should().ContainKey("IntakeBacklogCount");

			var routing = new IncidentNotificationRoutingPolicy();
			IncidentPolicyForm.Bind(routing, new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["Positions_41"] = "fsc, proc" }));
			routing.For(41).Should().Equal("FSC", "PROC");
			routing.For(36).Should().Equal(IncidentNotificationRoutingPolicy.Defaults[36]);
		}

		#endregion
	}
}
