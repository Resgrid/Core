using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using Scriban.Runtime;

namespace Resgrid.Tests.Rms
{
	/// <summary>Workflow integration for the RMS-5 triggers 161-163 (inspection completed, violation overdue, permit expiring): catalog, samples, and no content leakage.</summary>
	[TestFixture]
	public class RecordsRms5WorkflowTriggerTests
	{
		private static readonly WorkflowTriggerEventType[] Triggers = { WorkflowTriggerEventType.RecordInspectionCompleted, WorkflowTriggerEventType.RecordViolationOverdue, WorkflowTriggerEventType.RecordPermitExpiring };

		[Test]
		public void Every_rms5_trigger_is_a_records_trigger_with_a_catalog_a_protection_block_and_no_content()
		{
			foreach (var trigger in Triggers)
			{
				WorkflowTriggerEventTypes.IsRecordsTrigger(trigger).Should().BeTrue();
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger);
				catalog.Should().Contain(v => v.Name == "event.name").And.Contain(v => v.Name == "protection.is_protected").And.Contain(v => v.Name == "record.kind");
				catalog.Should().NotContain(v => v.Name.Contains("notes") || v.Name.Contains("description") || v.Name.Contains("applicant") || v.Name.Contains("signature") || v.Name.Contains("corrective"),
					$"{trigger} must never expose inspection notes, violation text or applicant identity");
			}
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordInspectionCompleted).Should().Contain(v => v.Name == "inspection.violation_count").And.NotContain(v => v.Name.StartsWith("permit."));
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordViolationOverdue).Should().Contain(v => v.Name == "violation.days_overdue").And.NotContain(v => v.Name.StartsWith("inspection."));
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordPermitExpiring).Should().Contain(v => v.Name == "permit.days_until_expiry").And.NotContain(v => v.Name.StartsWith("violation."));
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordExportScheduled).Should().NotContain(v => v.Name.StartsWith("inspection.") || v.Name.StartsWith("violation.") || v.Name.StartsWith("permit."));
		}

		[Test]
		public void Every_catalog_variable_has_a_sample_value()
		{
			foreach (var trigger in Triggers)
			{
				var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
				foreach (var descriptor in WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger))
					Has(data, descriptor.Name).Should().BeTrue($"{trigger} sample data must carry {descriptor.Name}");
				((ScriptObject)data["record"])["kind"].Should().Be("Prevention");
			}
		}

		private static bool Has(ScriptObject root, string path)
		{
			object current = root;
			foreach (var part in path.Split('.'))
			{
				if (current is ScriptObject obj && obj.ContainsKey(part)) { current = obj[part]; continue; }
				return false;
			}
			return true;
		}
	}
}
