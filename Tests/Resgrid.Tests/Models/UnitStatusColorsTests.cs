using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// A unit status's colours on the boards that list units with their status (nearest units, run card
	/// recommendations): the department's own option first, then the built-in one, hex colours only.
	/// </summary>
	[TestFixture]
	public class UnitStatusColorsTests
	{
		private static readonly Dictionary<int, CustomStateDetail> Custom = new Dictionary<int, CustomStateDetail>
		{
			{ 900, new CustomStateDetail { CustomStateDetailId = 900, ButtonText = "Standplaats", ButtonColor = "#FF0000", TextColor = "#000000" } },
			{ 901, new CustomStateDetail { CustomStateDetailId = 901, ButtonText = "Legacy", ButtonColor = "label-warning", TextColor = null } },
			{ 902, new CustomStateDetail { CustomStateDetailId = 902, ButtonText = "Odd", ButtonColor = "label-primary", TextColor = "red;display:none" } }
		};

		private static readonly Dictionary<int, CustomStateDetail> Defaults = UnitStatusColors.BuildDefaultMap(new[]
		{
			new CustomStateDetail { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonColor = "#d1dade", TextColor = "#5E5E5E" },
			null,
			new CustomStateDetail { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonColor = "#000000", TextColor = "#ffffff" }
		});

		[Test]
		public void a_custom_status_uses_its_own_colours()
		{
			UnitStatusColors.Resolve(900, Custom, Defaults).Should().Be(("#FF0000", "#000000"));
		}

		[Test]
		public void an_older_label_class_maps_to_its_colour_when_it_has_one()
		{
			UnitStatusColors.Resolve(901, Custom, Defaults).Should().Be(("#f0ad4e", (string)null));
			UnitStatusColors.Resolve(902, Custom, Defaults).Should().Be(((string)null, (string)null));
		}

		[Test]
		public void a_built_in_status_uses_the_built_in_colours_first_one_wins()
		{
			UnitStatusColors.Resolve((int)UnitStateTypes.Available, Custom, Defaults).Should().Be(("#d1dade", "#5E5E5E"));
		}

		[Test]
		public void a_status_with_no_option_has_no_colours()
		{
			UnitStatusColors.Resolve((int)UnitStateTypes.Delayed, Custom, Defaults).Should().Be(((string)null, (string)null));
			UnitStatusColors.Resolve(900, null, null).Should().Be(((string)null, (string)null));
		}

		[TestCase(" #abc ", "#abc")]
		[TestCase("#aabbccdd", "#aabbccdd")]
		[TestCase("#ggg", null)]
		[TestCase("#12345", null)]
		[TestCase("red", null)]
		[TestCase("", null)]
		[TestCase(null, null)]
		public void only_hex_colours_are_passed_on(string color, string expected)
		{
			UnitStatusColors.ToHexColor(color).Should().Be(expected);
		}
	}
}
