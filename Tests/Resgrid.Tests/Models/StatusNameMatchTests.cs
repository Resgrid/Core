using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// A reset saved before meanings ("Standing By", "Unavailable", a deleted option) on a set whose options have no meaning
	/// (RESGRID-WEBJOBS-BT / BV / C7): matched by name, only among options without a meaning.
	/// </summary>
	[TestFixture]
	public class StatusNameMatchTests
	{
		private static CustomStateDetail Option(int id, string text, int baseType = -1) =>
			new CustomStateDetail { CustomStateDetailId = id, ButtonText = text, BaseType = baseType };

		[Test]
		public void the_option_with_the_saved_name_wins()
		{
			var options = new List<CustomStateDetail> { Option(1, "Available"), Option(2, "Standing By"), Option(3, "Responding") };

			StatusNameMatch.Find(options, CustomStateTypes.Personnel, "Standing By", (int)ActionBaseTypes.Available).CustomStateDetailId.Should().Be(2,
				"the same name beats an earlier option that only suggests the same meaning");
			StatusNameMatch.Find(options, CustomStateTypes.Personnel, "STANDING-BY", null).CustomStateDetailId.Should().Be(2);
		}

		[Test]
		public void otherwise_the_first_option_whose_name_suggests_the_saved_meaning()
		{
			var options = new List<CustomStateDetail> { Option(1, "Responding"), Option(2, "In Service"), Option(3, "Available") };

			StatusNameMatch.Find(options, CustomStateTypes.Personnel, "Standing By", (int)ActionBaseTypes.Available).CustomStateDetailId.Should().Be(2);
			StatusNameMatch.Find(new[] { Option(10, "Duty Crew"), Option(11, "Away") }, CustomStateTypes.Staffing, "Unavailable", (int)StaffingBaseTypes.Unavailable)
				.CustomStateDetailId.Should().Be(11);
		}

		[Test]
		public void a_saved_value_without_a_meaning_uses_the_one_its_name_suggests()
		{
			// A deleted option "Avail" (no meaning) and the department's new "Available".
			StatusNameMatch.Find(new[] { Option(1, "Off"), Option(2, "Available") }, CustomStateTypes.Staffing, "Avail", null).CustomStateDetailId.Should().Be(2);
		}

		[Test]
		public void an_option_with_a_meaning_is_never_matched_by_name()
		{
			var options = new List<CustomStateDetail> { Option(1, "Standing By", (int)ActionBaseTypes.Standby), Option(2, "Available", (int)ActionBaseTypes.Unavailable) };

			StatusNameMatch.Find(options, CustomStateTypes.Personnel, "Standing By", (int)ActionBaseTypes.Available).Should().BeNull();
		}

		[TestCase("Code 7", null)]
		[TestCase("", null)]
		[TestCase(null, null)]
		public void no_name_and_no_meaning_match_nothing(string savedName, int? meaning)
		{
			StatusNameMatch.Find(new[] { Option(1, "Available"), Option(2, "Responding") }, CustomStateTypes.Personnel, savedName, meaning).Should().BeNull();
		}

		[Test]
		public void no_options_match_nothing()
		{
			StatusNameMatch.Find(null, CustomStateTypes.Personnel, "Standing By", (int)ActionBaseTypes.Available).Should().BeNull();
			StatusNameMatch.Find(new List<CustomStateDetail>(), CustomStateTypes.Staffing, "Unavailable", (int)StaffingBaseTypes.Unavailable).Should().BeNull();
		}
	}
}
