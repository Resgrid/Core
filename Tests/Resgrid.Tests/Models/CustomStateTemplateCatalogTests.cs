using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.CustomStates;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CustomStateTemplateCatalogTests
	{
		[Test]
		public void staffing_templates_carry_a_staffing_meaning_on_every_option()
		{
			var options = CustomStateTemplateCatalog.GetByType(CustomStateTypes.Staffing).SelectMany(t => t.Details).ToList();

			options.Should().NotBeEmpty();
			foreach (var option in options)
			{
				option.StaffingBaseType.Should().NotBeNull(option.ButtonText);
				option.StaffingBaseType.Should().NotBe(StaffingBaseTypes.None, option.ButtonText);
				option.ToCustomStateDetail().BaseType.Should().Be((int)option.StaffingBaseType.Value, option.ButtonText);
			}
		}

		[Test]
		public void staffing_template_meanings_follow_the_option_names()
		{
			var duty = CustomStateTemplateCatalog.GetById("staffing-duty").Details.ToDictionary(d => d.ButtonText, d => d.StaffingBaseType);

			duty["On Duty"].Should().Be(StaffingBaseTypes.OnShift);
			duty["On Call"].Should().Be(StaffingBaseTypes.OnCall);
			duty["On Break"].Should().Be(StaffingBaseTypes.Delayed);
			duty["Off Duty"].Should().Be(StaffingBaseTypes.OffDuty);
		}

		[TestCase(CustomStateTypes.Unit)]
		[TestCase(CustomStateTypes.Personnel)]
		[TestCase(CustomStateTypes.Staffing)]
		public void name_suggestions_agree_with_the_template_meanings(CustomStateTypes type)
		{
			// "Use suggested meanings" must not contradict a template that uses the same option name.
			foreach (var option in CustomStateTemplateCatalog.GetByType(type).SelectMany(t => t.Details))
			{
				var suggested = BaseTypeSuggestions.SuggestStatus(option.ButtonText, type);
				if (suggested.HasValue)
					suggested.Should().Be(option.ToCustomStateDetail().BaseType, option.ButtonText);
			}
		}

		[TestCase(CustomStateTypes.Unit)]
		[TestCase(CustomStateTypes.Personnel)]
		public void status_templates_store_their_action_base_type(CustomStateTypes type)
		{
			foreach (var option in CustomStateTemplateCatalog.GetByType(type).SelectMany(t => t.Details))
			{
				option.StaffingBaseType.Should().BeNull(option.ButtonText);
				Enum.IsDefined(typeof(ActionBaseTypes), option.BaseType).Should().BeTrue(option.ButtonText);
				option.ToCustomStateDetail().BaseType.Should().Be((int)option.BaseType, option.ButtonText);
			}
		}
	}
}
