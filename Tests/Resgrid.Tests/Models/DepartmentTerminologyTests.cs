using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>Department terminology (setting 118): the term catalog, name cleaning, and how saved names resolve.</summary>
	[TestFixture]
	public class DepartmentTerminologyTests
	{
		[Test]
		public void Catalog_keys_are_unique_and_cover_every_built_in_record_and_log_type()
		{
			DepartmentTerms.All.Select(t => t.Key).Should().OnlyHaveUniqueItems();
			DepartmentTerms.All.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.DefaultName));

			foreach (RmsOperationalRecordType type in Enum.GetValues(typeof(RmsOperationalRecordType)))
				DepartmentTerms.IsKnown(DepartmentTerms.RecordTypeKey(type)).Should().BeTrue(type.ToString());
			foreach (LogTypes type in Enum.GetValues(typeof(LogTypes)))
				DepartmentTerms.IsKnown(DepartmentTerms.RecordTypeKey(type)).Should().BeTrue(type.ToString());
			DepartmentTerms.IsKnown(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.NerisIncidentReport)).Should().BeTrue();
		}

		[Test]
		public void Record_type_keys_come_from_the_locked_definition_keys_and_department_keys_have_none()
		{
			DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run).Should().Be("record-type.run");
			DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.UnitActivity).Should().Be("record-type.unit-activity");
			DepartmentTerms.RecordTypeKey(LogTypes.Work).Should().Be("record-type.work", "legacy logs share the record type numbers");
			DepartmentTerms.RecordTypeKey("dept.public-education").Should().BeNull();
			DepartmentTerms.RecordTypeKey((string)null).Should().BeNull();
		}

		[TestCase("  Incident  ", "Incident")]
		[TestCase("Shift\t On\r\n Duty", "Shift On Duty")]
		[TestCase("Officers​ Meeting", "Officers Meeting")]
		[TestCase("Prevention/ Meetings", "Prevention/ Meetings")]
		[TestCase("   ", null)]
		[TestCase(null, null)]
		public void Names_are_cleaned_before_they_are_saved(string typed, string expected)
		{
			DepartmentTerms.Normalize(typed).Should().Be(expected);
		}

		[Test]
		public void A_saved_name_replaces_the_default_and_a_blank_one_restores_it()
		{
			var config = new DepartmentTerminologyConfig();
			var run = DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run);

			config.Set(run, "Incident");
			config.TextFor(run).Should().Be("Incident");
			config.RecordTypeName((int)RmsOperationalRecordType.Run).Should().Be("Incident");

			config.Set(run, "Incident Report");
			config.Terms.Should().ContainSingle(t => t.Key == run, "setting a term again replaces it");
			config.TextFor(run).Should().Be("Incident Report");

			config.Set(run, " ");
			config.TextFor(run).Should().BeNull();
			config.RecordTypeName((int)RmsOperationalRecordType.Run).Should().Be("Run");
		}

		[Test]
		public void Unknown_keys_and_names_over_the_limit_are_never_stored()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set("module.not-a-module", "Anything");
			config.Set(DepartmentTerms.Calls, new string('x', DepartmentTerms.MaxLength + 1));

			config.Terms.Should().BeEmpty();
		}

		[Test]
		public void A_stored_name_that_no_longer_validates_falls_back_to_the_default()
		{
			var config = new DepartmentTerminologyConfig();
			config.Terms.Add(new DepartmentTermOverride { Key = DepartmentTerms.Units, Text = new string('x', DepartmentTerms.MaxLength + 5) });
			config.Terms.Add(new DepartmentTermOverride { Key = DepartmentTerms.Calls, Text = "  " });

			config.TextFor(DepartmentTerms.Units, "Units").Should().Be("Units");
			config.TextFor(DepartmentTerms.Calls, "Calls").Should().Be("Calls");
		}

		[Test]
		public void Record_type_names_prefer_the_department_then_the_localized_then_the_shipped_name()
		{
			var config = new DepartmentTerminologyConfig();
			config.RecordTypeName((int)RmsOperationalRecordType.UnitActivity).Should().Be("Unit activity", "not the enum's UnitActivity");
			config.RecordTypeName((int)RmsOperationalRecordType.Meeting, "Besprechung").Should().Be("Besprechung");

			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Meeting), "Officers Meeting");
			config.RecordTypeName((int)RmsOperationalRecordType.Meeting, "Besprechung").Should().Be("Officers Meeting", "the department's word shows in every language");

			config.RecordTypeName(99).Should().Be("99", "an unknown number still renders something");
		}

		[Test]
		public void Definition_names_cover_locked_and_department_definitions()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Work), "Shift On Duty");

			config.DefinitionName(RmsDefinitionKeys.Work).Should().Be("Shift On Duty");
			config.DefinitionName(RmsDefinitionKeys.Training).Should().Be("Training");
			config.DefinitionName(RmsDefinitionKeys.NerisIncidentReport).Should().Be("NERIS incident report");
			config.DefinitionName("dept.public-education", "Public Education").Should().Be("Public Education");
			config.DefinitionName("dept.public-education").Should().Be("dept.public-education", "a definition with no name known falls back to its key");
		}

		[Test]
		public void The_setting_round_trips_through_protobuf()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run), "Incident");
			config.Set(DepartmentTerms.Units, "Apparatus");

			var copy = ObjectSerialization.Deserialize<DepartmentTerminologyConfig>(ObjectSerialization.Serialize(config));

			copy.TextFor(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run)).Should().Be("Incident");
			copy.TextFor(DepartmentTerms.Units).Should().Be("Apparatus");
			ObjectSerialization.Deserialize<DepartmentTerminologyConfig>(ObjectSerialization.Serialize(new DepartmentTerminologyConfig())).Terms.Should().BeEmpty();
		}

		[Test]
		public void The_locked_definition_catalog_lists_the_department_names()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run), "Incident");
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.NerisIncidentReport), "NFIRS Report");

			var catalog = RecordDefinitionCatalog.Describe(null, config);

			catalog.Single(d => d.Key == RmsDefinitionKeys.Run).Name.Should().Be("Incident");
			catalog.Single(d => d.Key == RmsDefinitionKeys.NerisIncidentReport).Name.Should().Be("NFIRS Report");
			catalog.Single(d => d.Key == RmsDefinitionKeys.UnitActivity).Name.Should().Be("Unit activity");
			RecordDefinitionCatalog.Describe().Single(d => d.Key == RmsDefinitionKeys.Run).Name.Should().Be("Run", "no terminology lists the shipped names");
		}
	}
}
