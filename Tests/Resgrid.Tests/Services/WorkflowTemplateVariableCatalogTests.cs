using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	namespace WorkflowTemplateVariableCatalogTests
	{
		[TestFixture]
		public class when_getting_variable_catalog
		{
			[Test]
			public void all_event_types_should_include_common_department_variables()
			{
				foreach (WorkflowTriggerEventType eventType in System.Enum.GetValues(typeof(WorkflowTriggerEventType)))
				{
					var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(eventType);

					catalog.Should().Contain(v => v.Name == "department.name",
						because: $"event type {eventType} must always include common department variables");
					catalog.Should().Contain(v => v.Name == "department.id");
					catalog.Should().Contain(v => v.Name == "timestamp.utc_now");
					catalog.Should().Contain(v => v.Name == "user.full_name");
				}
			}

			[Test]
			public void call_added_should_include_call_variables()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallAdded);
				catalog.Should().Contain(v => v.Name == "call.id");
				catalog.Should().Contain(v => v.Name == "call.name");
				catalog.Should().Contain(v => v.Name == "call.nature");
				catalog.Should().Contain(v => v.Name == "call.priority_text");
			}

			[Test]
			public void call_updated_should_include_same_call_variables_as_call_added()
			{
				var addedCatalog   = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallAdded);
				var updatedCatalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallUpdated);

				var addedCallVars   = addedCatalog.Where(v => v.Name.StartsWith("call.")).Select(v => v.Name).OrderBy(n => n).ToList();
				var updatedCallVars = updatedCatalog.Where(v => v.Name.StartsWith("call.")).Select(v => v.Name).OrderBy(n => n).ToList();

				addedCallVars.Should().BeEquivalentTo(updatedCallVars);
			}

			[Test]
			public void unit_status_changed_should_include_unit_variables()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.UnitStatusChanged);
				catalog.Should().Contain(v => v.Name == "unit.id");
				catalog.Should().Contain(v => v.Name == "unit_status.state_text");
				catalog.Should().Contain(v => v.Name == "previous_unit_status.state_text");
			}

			[Test]
			public void training_added_should_include_training_variables()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.TrainingAdded);
				catalog.Should().Contain(v => v.Name == "training.name");
				catalog.Should().Contain(v => v.Name == "training.minimum_score");
				catalog.Should().Contain(v => v.Name == "training.to_be_completed_by");
			}

			[Test]
			public void certification_expiring_should_include_certification_variables()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CertificationExpiring);
				catalog.Should().Contain(v => v.Name == "certification.days_until_expiry");
				catalog.Should().Contain(v => v.Name == "certification.expires_on");
			}

			[Test]
			public void all_common_variables_should_be_marked_is_common_true()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallAdded);
				var commonVars = catalog.Where(v => v.IsCommon).ToList();
				commonVars.Should().NotBeEmpty();
				commonVars.Should().AllSatisfy(v =>
					(v.Name.StartsWith("department.") || v.Name.StartsWith("timestamp.") || v.Name.StartsWith("user.")).Should().BeTrue());
			}

			[Test]
			public void event_specific_variables_should_not_be_common()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallAdded);
				catalog.Where(v => v.Name.StartsWith("call.")).Should().AllSatisfy(v => v.IsCommon.Should().BeFalse());
			}

			[Test]
			public void no_duplicate_variable_names_within_a_catalog()
			{
				foreach (WorkflowTriggerEventType eventType in System.Enum.GetValues(typeof(WorkflowTriggerEventType)))
				{
					var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(eventType);
					var names = catalog.Select(v => v.Name).ToList();
					names.Should().OnlyHaveUniqueItems(because: $"event type {eventType} must not have duplicate variable names");
				}
			}

			[Test]
			public void each_variable_should_have_non_empty_description_and_data_type()
			{
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallAdded);
				catalog.Should().AllSatisfy(v =>
				{
					v.Description.Should().NotBeNullOrWhiteSpace();
					v.DataType.Should().NotBeNullOrWhiteSpace();
				});
			}
		}
	}
}

