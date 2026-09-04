using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Pins the RMS identifier block exactly as the Identifier Allocation Registry assigns it, and pins
	/// every previously shipped WorkflowTriggerEventType value through trigger-baseline.json so a
	/// renumbering, which would silently re-point saved workflow definitions, fails CI.
	/// </summary>
	[TestFixture]
	public class RmsIdentifierPinTests
	{
		[Test]
		public void Permission_types_50_to_67_are_the_registry_names()
		{
			var expected = new Dictionary<int, string>
			{
				{ 50, "CreateRecord" }, { 51, "DeleteRecord" }, { 52, "ReviewRecords" }, { 53, "ApproveRecords" },
				{ 54, "FinalizeRecords" }, { 55, "AmendRecords" }, { 56, "SubmitRecords" }, { 57, "ExportRecords" },
				{ 58, "ShareRecordsExternally" }, { 59, "ViewRestrictedRecords" }, { 60, "ViewLegacyRecords" },
				{ 61, "ViewGroupRecords" }, { 62, "ManageRecordDefinitions" }, { 63, "PublishRecordDefinitions" },
				{ 64, "ManageRecordReports" }, { 65, "ManageRecordDisclosures" }, { 66, "ManageRecordLegalHold" },
				{ 67, "ReassignRecordDrafts" }
			};

			foreach (var kv in expected)
				Enum.GetName(typeof(PermissionTypes), kv.Key).Should().Be(kv.Value);

			// 40-49 belong to other pending plans; RMS must not have taken any of them.
			foreach (var value in Enumerable.Range(40, 10))
				Enum.IsDefined(typeof(PermissionTypes), value).Should().BeFalse($"PermissionTypes {value} is reserved for another plan");
		}

		[Test]
		public void Department_settings_70_to_77_are_the_registry_names()
		{
			var expected = new Dictionary<int, string>
			{
				{ 70, "RecordsDefaultLifecyclePreset" }, { 71, "RecordsReviewDueHours" }, { 72, "RecordsNumberingConfig" },
				{ 73, "RecordsSearchConfig" }, { 74, "RecordsRetentionPolicy" }, { 75, "RecordsGroupVisibilityMode" },
				{ 76, "RecordsGroupScopeConfig" }, { 77, "RecordsDisclosureConfig" }
			};

			foreach (var kv in expected)
				Enum.GetName(typeof(DepartmentSettingTypes), kv.Key).Should().Be(kv.Value);

			foreach (var value in Enumerable.Range(64, 6))
				Enum.IsDefined(typeof(DepartmentSettingTypes), value).Should().BeFalse($"DepartmentSettingTypes {value} is the cross-plan buffer");
		}

		[Test]
		public void Notification_event_types_31_to_33_are_the_registry_names()
		{
			Enum.GetName(typeof(EventTypes), 31).Should().Be("RecordReturnedForCorrection");
			Enum.GetName(typeof(EventTypes), 32).Should().Be("RecordReviewOverdue");
			Enum.GetName(typeof(EventTypes), 33).Should().Be("RecordSubmissionRejected");

			foreach (var value in Enumerable.Range(25, 6))
				Enum.IsDefined(typeof(EventTypes), value).Should().BeFalse($"EventTypes {value} belongs to Certifications or the buffer");
		}

		[Test]
		public void Workflow_triggers_in_the_rms_1_subset_are_the_registry_values()
		{
			((int)WorkflowTriggerEventType.RecordCreated).Should().Be(100);
			((int)WorkflowTriggerEventType.RecordSubmittedForReview).Should().Be(101);
			((int)WorkflowTriggerEventType.RecordReturnedForCorrection).Should().Be(102);
			((int)WorkflowTriggerEventType.RecordFinalized).Should().Be(104);
			((int)WorkflowTriggerEventType.RecordSubmissionQueued).Should().Be(108);
			((int)WorkflowTriggerEventType.RecordSubmissionAccepted).Should().Be(109);
			((int)WorkflowTriggerEventType.RecordSubmissionRejected).Should().Be(110);
			((int)WorkflowTriggerEventType.RecordSubmissionFailed).Should().Be(111);
			((int)WorkflowTriggerEventType.RecordAmended).Should().Be(105);
			((int)WorkflowTriggerEventType.RecordVoided).Should().Be(106);
			((int)WorkflowTriggerEventType.RecordCancelled).Should().Be(107);
			((int)WorkflowTriggerEventType.RecordOverdue).Should().Be(112);

			foreach (var value in Enumerable.Range(52, 48))
				Enum.IsDefined(typeof(WorkflowTriggerEventType), value).Should().BeFalse($"WorkflowTriggerEventType {value} is reserved for another plan");
		}

		[Test]
		public void Rms_worker_command_ids_are_the_registry_values()
		{
			// The RMS worker block is 40-43 plus the Unified Search allocation (44) that RMS-1 absorbed. These are
			// scheduled by integer in Workers/Resgrid.Workers.Console/Program.cs; the pin keeps that file honest.
			var program = ProgramSource();
			program.Should().Contain("new Commands.DomainEventOutboxDispatchCommand(40)");
			program.Should().Contain("new Commands.RmsSubmissionCommand(41)");
			program.Should().Contain("new Commands.RmsDueStateEvaluationCommand(42)");
			program.Should().Contain("new Commands.RmsRetentionAndPurgeCommand(43)");
			program.Should().Contain("new Commands.RecordsSearchIndexCommand(44)");
		}

		private static string ProgramSource()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			var path = Path.Combine(directory!.FullName, "Workers", "Resgrid.Workers.Console", "Program.cs");
			System.IO.File.Exists(path).Should().BeTrue("the worker host must exist at its documented path");
			return System.IO.File.ReadAllText(path);
		}

		[Test]
		public void Feature_flag_keys_match_the_registry()
		{
			FeatureFlagKeys.RecordsSystem.Should().Be("Records.System");
			FeatureFlagKeys.RecordsFieldResponder.Should().Be("Records.Field.Responder");
			FeatureFlagKeys.RecordsFieldUnit.Should().Be("Records.Field.Unit");
			FeatureFlagKeys.RecordsFieldIncidentCommand.Should().Be("Records.Field.IncidentCommand");
			FeatureFlagKeys.RecordsFieldDispatch.Should().Be("Records.Field.Dispatch");
		}

		[Test]
		public void Udf_entity_type_record_is_4()
		{
			((int)UdfEntityType.Record).Should().Be(4);
		}

		[Test]
		public void Permission_action_value_4_is_department_and_group_admins_and_select_roles()
		{
			((int)PermissionActions.DepartmentAndGroupAdminsAndSelectRoles).Should().Be(4);
		}

		[Test]
		public void Previously_shipped_workflow_trigger_values_never_change()
		{
			var baselinePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Allocations", "trigger-baseline.json");
			System.IO.File.Exists(baselinePath).Should().BeTrue("trigger-baseline.json must be copied to the test output (csproj Content item)");

			var baseline = JsonSerializer.Deserialize<Dictionary<string, int>>(System.IO.File.ReadAllText(baselinePath));
			baseline.Should().NotBeEmpty();

			foreach (var kv in baseline)
			{
				Enum.TryParse(typeof(WorkflowTriggerEventType), kv.Key, out var parsed).Should().BeTrue(
					$"trigger {kv.Key} was shipped and can never be removed (LogAdded = 11 in particular)");
				((int)parsed).Should().Be(kv.Value, $"trigger {kv.Key} was shipped as {kv.Value}; workflow definitions persist the integer");
			}

			// Every current value must be in the baseline: a new trigger is added by appending to the JSON.
			foreach (var name in Enum.GetNames(typeof(WorkflowTriggerEventType)))
				baseline.Should().ContainKey(name, $"new trigger {name} must be appended to trigger-baseline.json");
		}
	}
}
