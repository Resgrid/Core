using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CallPriorityMatchTests
	{
		// Bagley Fire and Rescue (dept 7966) names its priorities after incident kinds, 2026-10-13.
		private static List<DepartmentCallPriority> Bagley() => new List<DepartmentCallPriority>
		{
			new DepartmentCallPriority { DepartmentCallPriorityId = 291, Name = "Structure Fire", Sort = 0 },
			new DepartmentCallPriority { DepartmentCallPriorityId = 294, Name = "MVA", Sort = 0 },
			new DepartmentCallPriority { DepartmentCallPriorityId = 296, Name = "Haz Mat", Sort = 0 },
			new DepartmentCallPriority { DepartmentCallPriorityId = 297, Name = "Auto FIre", Sort = 0 },
			new DepartmentCallPriority { DepartmentCallPriorityId = 292, Name = "BFRS Medical", Sort = 1, IsDeleted = true },
			new DepartmentCallPriority { DepartmentCallPriorityId = 293, Name = "Medical", Sort = 1 },
			new DepartmentCallPriority { DepartmentCallPriorityId = 295, Name = "Brush / Woods Fire", Sort = 2 }
		};

		[TestCase("Medical Chest Pain", "Medical", 293)]
		[TestCase("Fire Structure Residential", "Fire", 291)]
		[TestCase("Fire Brush / Woods", "Fire", 295)]
		[TestCase("Fire Auto", "Fire", 297)]
		[TestCase("Medical MVA", "Medical", 293)] // the type word wins over another priority named in the line
		[TestCase("Fire Haz Mat Spill", "Fire", 296)]
		public void finds_the_priority_named_in_the_call_line(string line, string typeWord, int expected)
		{
			CallPriorityMatch.Find(Bagley(), line, typeWord).DepartmentCallPriorityId.Should().Be(expected);
		}

		[TestCase("Fire Tree Down")]
		[TestCase("S20I Traffic Accident Injuries")]
		public void a_line_naming_no_priority_gets_none(string line)
		{
			CallPriorityMatch.Find(Bagley(), line, line.Split(' ')[0]).Should().BeNull();
		}

		[Test]
		public void deleted_priorities_never_match()
		{
			// "BFRS Medical" is deleted, so the line falls to the active "Medical".
			CallPriorityMatch.Find(Bagley(), "BFRS Medical Call", "BFRS").DepartmentCallPriorityId.Should().Be(293);
		}

		[Test]
		public void the_most_specific_name_wins()
		{
			var priorities = new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 1, Name = "Fire" },
				new DepartmentCallPriority { DepartmentCallPriorityId = 2, Name = "Structure Fire" }
			};

			CallPriorityMatch.Find(priorities, "Commercial structure fire").DepartmentCallPriorityId.Should().Be(2);
		}

		[Test]
		public void default_for_import_prefers_the_departments_default_then_its_high_meaning()
		{
			var priorities = Bagley();
			CallPriorityMatch.DefaultForImport(priorities).Should().Be((int)CallPriority.High, "no default and no High meaning keeps the old built-in High");

			priorities[5].BaseType = (int)CallPriority.High;
			CallPriorityMatch.DefaultForImport(priorities).Should().Be(293);

			priorities[1].IsDefault = true;
			CallPriorityMatch.DefaultForImport(priorities).Should().Be(294);

			priorities[1].IsDeleted = true;
			CallPriorityMatch.DefaultForImport(priorities).Should().Be(293, "a deleted default is ignored");
		}

		[Test]
		public void default_for_import_without_priorities_is_built_in_high()
		{
			CallPriorityMatch.DefaultForImport(null).Should().Be((int)CallPriority.High);
			CallPriorityMatch.DefaultForImport(new List<DepartmentCallPriority>()).Should().Be((int)CallPriority.High);
		}
	}
}
