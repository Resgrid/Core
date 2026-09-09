using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChecklistValidationTests
	{
		private static ChecklistForm Form(params ChecklistItem[] items) => new ChecklistForm { Name = "Readiness", Sections = { new ChecklistSection { Name = "Checks", Items = items.ToList() } } };
		private static ChecklistItem Item(ChecklistItemType type = ChecklistItemType.PassFail) => new ChecklistItem { Name = "Check", Type = type };
		private static ChecklistRunInput Answer(ChecklistItem item, string value, ChecklistAnswerStatus status = ChecklistAnswerStatus.Answered) => new ChecklistRunInput { Answers = { new ChecklistAnswer { ItemId = item.Id, Value = value, Status = status, Note = "Failure reason" } } };
		[TestCase(ChecklistItemType.PassFail, "pass", true)]
		[TestCase(ChecklistItemType.PassFail, "fail", false)]
		[TestCase(ChecklistItemType.YesNo, "true", true)]
		[TestCase(ChecklistItemType.Checkbox, "false", false)]
		[TestCase(ChecklistItemType.NumericReading, "5.5", true)]
		[TestCase(ChecklistItemType.Quantity, "12", false)]
		[TestCase(ChecklistItemType.FreeText, "Checked", true)]
		[TestCase(ChecklistItemType.SelectList, "true", true)]
		[TestCase(ChecklistItemType.DateValue, "2026-09-08", true)]
		[TestCase(ChecklistItemType.Photo, null, true)]
		[TestCase(ChecklistItemType.Signature, null, true)]
		public void Every_answer_type_has_explicit_server_side_pass_semantics(ChecklistItemType type, string value, bool passed)
		{
			var item = Item(type); item.Minimum = 1; item.Maximum = 10; item.Options = new List<string> { "true", "false" };
			var result = ChecklistValidation.Evaluate(Form(item), Answer(item, value), new HashSet<string> { item.Id }, true);
			result.Errors.Should().BeEmpty(); result.Passed.Should().Be(passed);
		}
		[Test]
		public void Missing_answer_never_passes_and_all_na_has_no_score()
		{
			var item = Item(); item.Required = false; item.AllowNotApplicable = true;
			var result = ChecklistValidation.Evaluate(Form(item), new ChecklistRunInput(), new HashSet<string>(), true); result.Score.Should().Be(0); result.Passed.Should().BeFalse();
			var answer = Answer(item, null, ChecklistAnswerStatus.NotApplicable); answer.Answers[0].NotApplicableReason = "Not fitted";
			result = ChecklistValidation.Evaluate(Form(item), answer, new HashSet<string>(), true); result.Errors.Should().BeEmpty(); result.Score.Should().BeNull(); result.Passed.Should().BeFalse();
		}
		[Test]
		public void Critical_failure_overrides_an_otherwise_passing_weighted_score()
		{
			var critical = Item(); critical.Critical = true; var other = Item(); other.Weight = 99;
			var form = Form(critical, other); form.PassThreshold = 90;
			var input = Answer(critical, "fail"); input.Answers.Add(Answer(other, "pass").Answers[0]);
			var result = ChecklistValidation.Evaluate(form, input, new HashSet<string>(), true); result.Score.Should().Be(99); result.Passed.Should().BeFalse();
		}
		[Test]
		public void Zero_threshold_does_not_turn_an_unanswered_checklist_into_a_pass()
		{
			var item = Item(); item.Required = false;
			var form = Form(item); form.PassThreshold = 0;
			ChecklistValidation.Evaluate(form, new ChecklistRunInput(), new HashSet<string>(), true).Passed.Should().BeFalse();
		}
		[Test]
		public void Na_requires_a_reason_and_permission_and_cannot_carry_a_value()
		{
			var item = Item(); var input = Answer(item, "pass", ChecklistAnswerStatus.NotApplicable);
			ChecklistValidation.Evaluate(Form(item), input, new HashSet<string>(), true).Errors.Should().NotBeEmpty();
			item.AllowNotApplicable = true; input.Answers[0].NotApplicableReason = "Not fitted";
			ChecklistValidation.Evaluate(Form(item), input, new HashSet<string>(), true).Errors.Should().NotBeEmpty();
		}
		[Test]
		public void Conditions_control_visibility_and_requiredness_without_evaluating_code()
		{
			var source = Item(); var conditional = Item(); conditional.Required = false; conditional.VisibleWhen = new ChecklistCondition { ItemId = source.Id, EqualsValue = "fail" }; conditional.RequiredWhen = new ChecklistCondition { ItemId = source.Id, EqualsValue = "fail" };
			var form = Form(source, conditional); ChecklistValidation.Validate(form).Should().BeEmpty();
			ChecklistValidation.Evaluate(form, Answer(source, "pass"), new HashSet<string>(), true).Passed.Should().BeTrue();
			ChecklistValidation.Evaluate(form, Answer(source, "fail"), new HashSet<string>(), true).Errors.Should().Contain(e => e.Contains("required"));
			source.VisibleWhen = new ChecklistCondition { ItemId = conditional.Id, EqualsValue = "pass" }; ChecklistValidation.Validate(form).Should().NotBeEmpty();
		}
		[Test]
		public void Duplicated_ids_unknown_ids_invalid_values_and_oversized_forms_are_rejected()
		{
			var item = Item(); var form = Form(item, item); ChecklistValidation.Validate(form).Should().NotBeEmpty();
			form = Form(item); var input = Answer(item, "maybe"); ChecklistValidation.Evaluate(form, input, new HashSet<string>(), true).Errors.Should().NotBeEmpty();
			input.Answers[0].ItemId = Guid.NewGuid().ToString(); ChecklistValidation.Evaluate(form, input, new HashSet<string>(), true).Errors.Should().NotBeEmpty();
			form.Sections[0].Items = Enumerable.Range(0, 251).Select(_ => Item()).ToList(); ChecklistValidation.Validate(form).Should().NotBeEmpty();
		}
		[Test]
		public void Applying_templates_copies_content_and_generates_department_owned_ids()
		{
			foreach (var template in ChecklistTemplateCatalog.All)
			{
				var form = ChecklistForm.FromTemplate(template); ChecklistValidation.Validate(form).Should().BeEmpty(template.Name);
				form.Sections.Select(s => s.Id).Intersect(template.Sections.Select(s => s.SectionId)).Should().BeEmpty();
				form.Sections.SelectMany(s => s.Items).Select(i => i.Id).Intersect(template.Sections.SelectMany(s => s.Items).Select(i => i.ItemId)).Should().BeEmpty();
			}
		}
		[Test]
		public void Every_persisted_content_slot_and_binary_has_a_catalog_and_upgrade_binding()
		{
			var catalog = new ProtectedFieldCatalog(); var bindings = AdpTableBindings.ForVersionRange(catalog, 13, catalog.Version);
			foreach (var table in ChecklistTables.All.Values)
			{
				bindings.Should().ContainSingle(b => b.TableName == table && b.DepartmentColumn == "DepartmentId" && b.ProtectedMarkerColumn == "IsProtected");
				bindings.Single(b => b.TableName == table).Columns.Should().Contain(c => c.FieldId == table.ToLowerInvariant() + ".content");
			}
			bindings.Single(b => b.TableName == "ChecklistCompletionFiles").Columns.Should().Contain(c => c.FieldId == "checklistcompletionfiles.data");
		}
	}
}
