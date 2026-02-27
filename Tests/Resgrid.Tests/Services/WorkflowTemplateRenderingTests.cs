using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class WorkflowTemplateRenderingTests
	{
		private static ScriptObject BuildSampleContext()
			=> (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.CallAdded);

		[Test]
		public void SimpleVariableSubstitution_RendersCorrectly()
		{
			var context  = BuildSampleContext();
			var template = Template.Parse("Hello {{ user.full_name }}");
			var result   = template.Render(context);

			result.Should().Contain("Hello ");
			result.Should().NotContain("{{");
		}

		[Test]
		public void DepartmentVariables_RenderInEmailBody()
		{
			var context  = BuildSampleContext();
			var template = Template.Parse("<h1>{{ department.name }}</h1><p>{{ department.address.full }}</p>");
			var result   = template.Render(context);

			result.Should().NotBeNullOrEmpty();
			result.Should().NotContain("{{");
		}

		[Test]
		public void CallVariables_RenderInApiPostBody()
		{
			var context  = BuildSampleContext();
			var tmplText = "{ \"call\": \"{{ call.name }}\", \"priority\": \"{{ call.priority_text }}\" }";
			var template = Template.Parse(tmplText);
			var result   = template.Render(context);

			result.Should().NotContain("{{");
			result.Should().Contain("call");
		}

		[Test]
		public void ConditionalLogic_RendersCorrectly()
		{
			var context  = BuildSampleContext();
			var template = Template.Parse("{% if call.is_critical %}CRITICAL{% else %}Normal{% end %}");
			var result   = template.Render(context);

			result.Should().BeOneOf("CRITICAL", "Normal");
		}

		[Test]
		public void MissingVariable_RendersAsEmpty()
		{
			var context  = BuildSampleContext();
			var template = Template.Parse("{{ call.nonexistent_field_xyz }}");
			var result   = template.Render(context);

			result.Should().BeEmpty();
		}

		[Test]
		public void InvalidTemplateSyntax_IsCapturedByParse()
		{
			var template = Template.Parse("{{ unclosed");
			template.HasErrors.Should().BeTrue("unclosed template expression should yield parse errors");
		}

		[Test]
		public void NestedObjectAccess_RendersCorrectly()
		{
			var context  = BuildSampleContext();
			var template = Template.Parse("{{ department.address.city }}");
			var result   = template.Render(context);

			result.Should().NotContain("{{");
		}

		[Test]
		public void DateFormatting_WorksWithScribanFilter()
		{
			var context  = BuildSampleContext();
			var template = Template.Parse("{{ call.logged_on | date.to_string \"%Y-%m-%d\" }}");
			var result   = template.Render(context);

			// Result should look like a date or be empty if no call data — either is acceptable without throwing
			result.Should().NotContain("{{");
		}

		[Test]
		public void LargeTemplate_RendersWithinTimeLimit()
		{
			var context  = BuildSampleContext();
			var lines    = Enumerable.Range(1, 100).Select(i => $"Line {i}: {{{{ department.name }}}} {{{{ call.name }}}}");
			var tmplText = string.Join("\n", lines);

			var sw = System.Diagnostics.Stopwatch.StartNew();
			var template = Template.Parse(tmplText);
			var result   = template.Render(context);
			sw.Stop();

			sw.ElapsedMilliseconds.Should().BeLessThan(2000, "rendering 100 lines should complete well within 2 seconds");
			result.Should().NotBeNullOrEmpty();
		}

		[Test]
		public void AllEventTypes_RenderSampleTemplateWithoutError()
		{
			var tmplText  = "{{ department.name }} - {{ timestamp.utc_now }}";
			var template  = Template.Parse(tmplText);

			foreach (WorkflowTriggerEventType eventType in Enum.GetValues(typeof(WorkflowTriggerEventType)))
			{
				var context = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(eventType);
				context.Should().NotBeNull($"sample data for {eventType} should not be null");

				Action act = () => template.Render(context);
				act.Should().NotThrow($"rendering for event type {eventType} should not throw");
			}
		}
	}
}



