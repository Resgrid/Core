using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UnitRoleStaffingResultTests
	{
		private const int UnitId = 42;

		/// <summary>
		/// Regression: two defined seats sharing the same Name must not both be satisfied by a single
		/// active assignment. Before the fix, active.FirstOrDefault matched the same UnitActiveRole for
		/// every duplicate seat and overcounted FilledRoleCount (reporting the unit as fully staffed).
		/// </summary>
		[Test]
		public void Calculate_DuplicateRoleNames_SingleActiveAssignmentFillsOnlyOneSeat()
		{
			var definedRoles = new List<UnitRole>
			{
				new UnitRole { UnitId = UnitId, Name = "Firefighter" },
				new UnitRole { UnitId = UnitId, Name = "Firefighter" }
			};

			var activeRoles = new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = UnitId, Role = "Firefighter", UserId = "user-1" }
			};

			var result = UnitRoleStaffingResult.Calculate(UnitId, definedRoles, activeRoles, (userId, personnelRoleId) => true);

			result.DefinedRoleCount.Should().Be(2);
			result.FilledRoleCount.Should().Be(1, "one active assignment must not fill two identically-named seats");
			result.UnfilledRoleNames.Should().ContainSingle().Which.Should().Be("Firefighter");
			result.Level.Should().Be(UnitStaffingLevel.PartiallyStaffed);
		}

		/// <summary>
		/// Companion happy-path: consuming a matched assignment must not prevent a second, distinct
		/// assignment from filling the other duplicate seat.
		/// </summary>
		[Test]
		public void Calculate_DuplicateRoleNames_TwoActiveAssignmentsFillBothSeats()
		{
			var definedRoles = new List<UnitRole>
			{
				new UnitRole { UnitId = UnitId, Name = "Firefighter" },
				new UnitRole { UnitId = UnitId, Name = "Firefighter" }
			};

			var activeRoles = new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = UnitId, Role = "Firefighter", UserId = "user-1" },
				new UnitActiveRole { UnitId = UnitId, Role = "Firefighter", UserId = "user-2" }
			};

			var result = UnitRoleStaffingResult.Calculate(UnitId, definedRoles, activeRoles, (userId, personnelRoleId) => true);

			result.DefinedRoleCount.Should().Be(2);
			result.FilledRoleCount.Should().Be(2);
			result.UnfilledRoleNames.Should().BeEmpty();
			result.Level.Should().Be(UnitStaffingLevel.FullyStaffed);
		}
	}
}
