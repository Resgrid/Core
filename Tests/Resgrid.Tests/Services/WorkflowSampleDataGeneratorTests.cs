using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class WorkflowSampleDataGeneratorTests
	{
		[Test]
		public void GenerateSampleData_ReturnsNonNullForAllEventTypes()
		{
			foreach (WorkflowTriggerEventType eventType in Enum.GetValues(typeof(WorkflowTriggerEventType)))
			{
				var result = WorkflowSampleDataGenerator.GenerateSampleData(eventType);
				result.Should().NotBeNull($"sample data for {eventType} should not be null");
			}
		}

		[Test]
		public void GenerateSampleData_IncludesDepartmentVariables()
		{
			var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.CallAdded);
			data["department"].Should().NotBeNull();
		}

		[Test]
		public void GenerateSampleData_IncludesUserVariables()
		{
			var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.CallAdded);
			data["user"].Should().NotBeNull();
		}

		[Test]
		public void GenerateSampleData_CallAdded_IncludesCallVariables()
		{
			var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.CallAdded);
			data["call"].Should().NotBeNull();
		}

		[Test]
		public void GenerateSampleData_UnitStatusChanged_IncludesUnitVariables()
		{
			var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.UnitStatusChanged);
			data["unit"].Should().NotBeNull();
		}

		[Test]
		public void GenerateSampleData_PersonnelStaffingChanged_IncludesStaffingVariables()
		{
			var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.PersonnelStaffingChanged);
			data["staffing"].Should().NotBeNull();
		}

		[Test]
		public void GenerateSampleData_CanRenderSimpleTemplateWithoutError()
		{
			var template = Template.Parse("{{ department.name }} - {{ timestamp.utc_now }}");
			template.HasErrors.Should().BeFalse();

			foreach (WorkflowTriggerEventType eventType in Enum.GetValues(typeof(WorkflowTriggerEventType)))
			{
				var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(eventType);
				Action act = () => template.Render(data);
				act.Should().NotThrow($"rendering for {eventType} should not throw");
			}
		}

		[Test]
		public void GenerateSampleData_DepartmentName_IsRealisticValue()
		{
			var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.CallAdded);
			var dept = data["department"] as ScriptObject;
			dept.Should().NotBeNull();

			var name = dept?["name"]?.ToString();
			name.Should().NotBeNullOrWhiteSpace();
			name.Should().NotBe("string");
			name.Should().NotBe("test");
			name.Should().NotBe("value");
		}
	}
}

