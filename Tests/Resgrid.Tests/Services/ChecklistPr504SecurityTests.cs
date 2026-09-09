using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		internal sealed partial class MemoryStore
		{
			public Task<List<T>> ListForMemberAsync<T>(int departmentId, string userId, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow
			{
				IEnumerable<TRow> Rows<TRow>() where TRow : ChecklistRow => _rows.Where(p => p.Key.Item1 == typeof(TRow))
					.Select(p => JsonConvert.DeserializeObject<TRow>(p.Value)).Where(r => r.DepartmentId == departmentId);
				bool Target(int type, string id) => type == (int)ChecklistTargetType.Personnel && id == userId;
				var ownedOccurrences = Rows<ChecklistCompletion>().Where(c => c.CreatedBy == userId || Target(c.TargetType, c.TargetId)).Select(c => c.OccurrenceId).ToHashSet();
				var ownedSchedules = Rows<ChecklistSchedule>().Where(s => s.CreatedBy == userId || Target(s.TargetType, s.TargetId)).Select(s => s.Id).ToHashSet();
				return Task.FromResult(Rows<T>().Where(row => row switch
				{
					ChecklistCompletion c => c.CreatedBy == userId || c.WitnessUserId == userId || Target(c.TargetType, c.TargetId),
					ChecklistSchedule s => s.CreatedBy == userId || Target(s.TargetType, s.TargetId),
					ChecklistOccurrence o => Target(o.TargetType, o.TargetId) || ownedOccurrences.Contains(o.Id) || ownedSchedules.Contains(o.ScheduleId),
					_ => throw new InvalidOperationException("This checklist table does not support member exports.")
				}).OrderByDescending(r => r.CreatedOn).Skip(skip).Take(take).ToList());
			}
		}
	}

	public partial class ChecklistEventDeliveryTests
	{
		[TestCase(0, "42", true), TestCase(1, "42", true), TestCase(2, "42", true)]
		[TestCase(3, "person-42", false), TestCase(3, "42", false), TestCase(3, "ca0cdd20-5916-4c12-8c03-7d9a982d845c", false)]
		[TestCase(0, "free-form-target", false), TestCase(1, "0", false), TestCase(2, "-4", false)]
		[TestCase(5, "ca0cdd20-5916-4c12-8c03-7d9a982d845c", true), TestCase(5, "free-form-target", false)]
		[TestCase(4, "42", false), TestCase(99, "42", false)]
		public async Task Workflow_targets_use_the_same_structural_rules_before_department_enrollment(int type, string id, bool structural)
		{
			_policy.Setup(p => p.IsProtectionEnforcedAsync(42)).ReturnsAsync(false);
			var payload = new JObject { ["TargetType"] = type, ["TargetId"] = id, ["Score"] = 87.25m, ["Passed"] = false };
			var projected = JObject.Parse(await ChecklistWorkflowPayload.ProjectAsync(42, payload, _projection));
			var routing = JObject.Parse(ChecklistWorkflowPayload.Routing(payload.ToString(), null));
			projected["TargetId"].Value<string>().Should().Be(structural ? id : ProtectedDataEnvelope.RedactionValue);
			projected["TargetId"].Should().BeEquivalentTo(routing["TargetId"]);
			projected["Score"].Value<decimal>().Should().Be(87.25m);
			projected["Passed"].Value<bool>().Should().BeFalse();
			projected["is_redacted"].Value<bool>().Should().Be(!structural);
			projected["redacted_fields"].Values<string>().Should().BeEquivalentTo(structural ? Array.Empty<string>() : new[] { "TargetId" });
		}

		[Test]
		public async Task Workflow_target_length_limit_and_wrapped_redaction_survive_reprojection()
		{
			_policy.Setup(p => p.IsProtectionEnforcedAsync(42)).ReturnsAsync(false);
			var source = new JObject { ["Payload"] = new JObject { ["TargetType"] = 3, ["TargetId"] = "person-42" } };
			var first = JObject.Parse(await ChecklistWorkflowPayload.ProjectAsync(42, source, _projection, true));
			var second = JObject.Parse(await ChecklistWorkflowPayload.ProjectAsync(42, first, _projection, true));
			second["Payload"]["TargetId"].Value<string>().Should().Be(ProtectedDataEnvelope.RedactionValue);
			second["Payload"]["redacted_fields"].Values<string>().Should().Contain("TargetId");
			source["Payload"]["TargetId"] = new string('x', 129);
			JObject.Parse(await ChecklistWorkflowPayload.ProjectAsync(42, source, _projection, true))["Payload"]["TargetId"].Should().BeNull();
		}

		[Test]
		public void Readiness_trigger_membership_cannot_be_replaced_through_the_public_collection()
		{
			var collection = (IList<int>)ChecklistWorkflowPayload.Triggers;
			Action change = () => collection[0] = 999;
			change.Should().Throw<NotSupportedException>();
			ChecklistWorkflowPayload.Triggers.Should().Equal(67, 68, 69, 70, 71, 72, 164, 165, 22, 58, 59, 60, 64, 66);
			ChecklistWorkflowPayload.IsChecklist(67).Should().BeTrue();
			ChecklistWorkflowPayload.IsChecklist(999).Should().BeFalse();
		}
	}

	public partial class GdprExportProtectedDataTests
	{
		[TestCase(false), TestCase(true)]
		public async Task Checklist_witness_only_export_contains_own_witness_facts_without_other_members_records(bool protectedDepartment)
		{
			var store = new ChecklistWorkflowTests.MemoryStore();
			var witnessTime = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
			var witnessedIds = new HashSet<string>();
			for (var i = 0; i < 105; i++)
			{
				var completion = new ChecklistCompletion { DepartmentId = DeptId, CreatedBy = "OTHER-MEMBER-CANARY", WitnessUserId = UserId, WitnessedOn = witnessTime,
					TargetType = (int)ChecklistTargetType.Personnel, TargetId = "OTHER-TARGET-CANARY", Content = "OTHER-CONTENT-CANARY", SubmittedOn = witnessTime.AddMinutes(-1), OccurrenceId = Guid.NewGuid().ToString() };
				witnessedIds.Add(completion.Id); await store.WriteAsync(completion, true);
				await store.WriteAsync(new ChecklistCompletionItem { DepartmentId = DeptId, ParentId = completion.Id, Content = "OTHER-ANSWER-CANARY" }, true);
				await store.WriteAsync(new ChecklistCompletionFile { DepartmentId = DeptId, ParentId = completion.Id, Content = "OTHER-FILE-CANARY", Data = new byte[] { 1, 2, 3 } }, true);
				await store.WriteAsync(new ChecklistOccurrence { DepartmentId = DeptId, Id = completion.OccurrenceId, TargetType = completion.TargetType, TargetId = completion.TargetId }, true);
			}
			var created = new ChecklistCompletion { DepartmentId = DeptId, CreatedBy = UserId, WitnessUserId = UserId, TargetType = (int)ChecklistTargetType.Personnel, TargetId = "owned-target", OccurrenceId = Guid.NewGuid().ToString() };
			var targeted = new ChecklistCompletion { DepartmentId = DeptId, CreatedBy = "another-author", WitnessUserId = UserId, TargetType = (int)ChecklistTargetType.Personnel, TargetId = UserId, OccurrenceId = Guid.NewGuid().ToString() };
			foreach (var completion in new[] { created, targeted })
			{
				await store.WriteAsync(completion, true);
				await store.WriteAsync(new ChecklistCompletionItem { DepartmentId = DeptId, ParentId = completion.Id }, true);
				await store.WriteAsync(new ChecklistCompletionFile { DepartmentId = DeptId, ParentId = completion.Id }, true);
				await store.WriteAsync(new ChecklistOccurrence { DepartmentId = DeptId, Id = completion.OccurrenceId, TargetType = completion.TargetType, TargetId = completion.TargetId }, true);
			}
			await store.WriteAsync(new ChecklistCompletion { DepartmentId = 999, WitnessUserId = UserId, CreatedBy = "FOREIGN-CANARY" }, true);
			var policy = new Mock<IDepartmentDataProtectionService>(); policy.Setup(p => p.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(protectedDepartment);
			var protection = new ReadinessHistoryProtectionService(Mock.Of<IProtectedWriteService>(), policy.Object);
			var reminders = new Mock<IChecklistReminderRepository>();
			reminders.Setup(r => r.ForRecipientAsync(DeptId, UserId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChecklistReminder>());
			_service = new GdprDataExportService(_repository.Object, _userProfileService.Object, _memberSensitiveDataService.Object, _emergencyContactService.Object,
				_usersService.Object, _departmentsService.Object, _departmentGroupsService.Object, _personnelRolesService.Object, _actionLogsService.Object,
				_messageService.Object, _certificationService.Object, _trainingService.Object, _shiftsService.Object, _emailService.Object, store,
				new Lazy<IReadinessHistoryProtectionService>(() => protection), reminders.Object, EmptyWorkOrders(), EmptyInventory());
			var json = (await RunExportAsync())["checklists.json"]; var exported = JObject.Parse(json);
			json.Should().NotContain("CANARY").And.NotContain("AQID");
			exported["Witnesses"].Should().HaveCount(105);
			exported["Witnesses"].Select(w => w["CompletionId"].Value<string>()).Should().BeEquivalentTo(witnessedIds);
			foreach (var witness in exported["Witnesses"].Cast<JObject>())
			{
				witness.Properties().Select(p => p.Name).Should().BeEquivalentTo("CompletionId", "WitnessUserId", "WitnessedOn");
				witness["WitnessUserId"].Value<string>().Should().Be(UserId);
				witness["WitnessedOn"].Value<DateTime>().Should().Be(witnessTime);
			}
			exported["Completions"].Select(c => c["Completion"]["Id"].Value<string>()).Should().BeEquivalentTo(created.Id, targeted.Id);
			foreach (var completion in exported["Completions"])
			{
				completion["Answers"].Should().HaveCount(1); completion["Files"].Should().HaveCount(1);
			}
			exported["Occurrences"].Select(o => o["Id"].Value<string>()).Should().BeEquivalentTo(created.OccurrenceId, targeted.OccurrenceId);
			store.ChildQueries.Should().Be(2, "witness-only completions must never load their children");
		}
	}

	[TestFixture, NonParallelizable]
	public class ChecklistAdpDiscriminatorReviewTests
	{
		[TestCase(DatabaseTypes.SqlServer), TestCase(DatabaseTypes.Postgres)]
		public async Task Invalid_shared_table_discriminators_fail_before_opening_a_database(DatabaseTypes type)
		{
			var previous = DataConfig.DatabaseType;
			try
			{
				DataConfig.DatabaseType = type;
				var connections = new Mock<IConnectionProvider>(MockBehavior.Strict);
				var repository = new DepartmentDataProtectionBulkRepository(connections.Object, type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration());
				var binding = AdpTableBinding.Direct("WorkflowRuns", "WorkflowRunId", false, "DepartmentId", Array.Empty<AdpColumnSpec>());
				foreach (var filter in new[]
				{
					new AdpRowDiscriminator("TriggerEventType", new[] { 67 }, OnParent: true),
					new AdpRowDiscriminator("TriggerEventType", Array.Empty<int>()),
					new AdpRowDiscriminator("TriggerEventType"),
					new AdpRowDiscriminator("ProducerSubsystem", Texts: Array.Empty<string>()),
					new AdpRowDiscriminator("ProducerSubsystem", Texts: new string[] { null })
				})
				{
					Func<Task> count = () => repository.CountRowsAsync(binding with { Discriminator = filter }, 77);
					await count.Should().ThrowAsync<InvalidOperationException>();
				}
				connections.Verify(p => p.Create(), Times.Never);
			}
			finally { DataConfig.DatabaseType = previous; }
		}
	}
}
