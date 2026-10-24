using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A person's map pin reads "Name (Unit)" only while they hold an active role on a unit the viewer may see.
	/// </summary>
	[TestFixture]
	public class PersonnelMapLabelTests
	{
		private static readonly List<Unit> Units = new List<Unit>
		{
			new Unit { UnitId = 1, Name = "Engine 1" },
			new Unit { UnitId = 2, Name = "Medic 2" },
			new Unit { UnitId = 3, Name = "Old Truck", IsDeleted = true },
			new Unit { UnitId = 4, Name = "Hidden Squad" }
		};

		private static Task<bool> CanView(int unitId) => Task.FromResult(unitId != 4);

		private static Task<Dictionary<string, string>> Build(params UnitActiveRole[] seats) =>
			PersonnelMapLabel.BuildUnitNamesByUserAsync(seats, Units, CanView);

		[Test]
		public async Task A_seated_member_is_labelled_with_their_unit()
		{
			var names = await Build(new UnitActiveRole { UnitId = 1, UserId = "shawn", Role = "Operator" });

			PersonnelMapLabel.Format("Shawn Jackson", "shawn", names).Should().Be("Shawn Jackson (Engine 1)");
		}

		[Test]
		public async Task A_member_with_no_active_role_keeps_their_plain_name()
		{
			var names = await Build(new UnitActiveRole { UnitId = 1, UserId = "shawn", Role = "Operator" });

			PersonnelMapLabel.Format("Pat Smith", "pat", names).Should().Be("Pat Smith");
		}

		[Test]
		public async Task Seats_on_deleted_unknown_or_hidden_units_label_no_one()
		{
			var names = await Build(
				new UnitActiveRole { UnitId = 3, UserId = "deleted-seat", Role = "Driver" },
				new UnitActiveRole { UnitId = 99, UserId = "unknown-seat", Role = "Driver" },
				new UnitActiveRole { UnitId = 4, UserId = "hidden-seat", Role = "Driver" },
				new UnitActiveRole { UnitId = 1, UserId = "  ", Role = "Officer" });

			names.Should().BeEmpty();
			PersonnelMapLabel.Format("Hidden Person", "hidden-seat", names).Should().Be("Hidden Person");
		}

		[Test]
		public async Task Two_seats_on_one_unit_name_it_once_and_seats_on_two_units_name_both_in_order()
		{
			var names = await Build(
				new UnitActiveRole { UnitId = 1, UserId = "shawn", Role = "Operator" },
				new UnitActiveRole { UnitId = 1, UserId = "shawn", Role = "Officer" },
				new UnitActiveRole { UnitId = 2, UserId = "jo", Role = "Medic" },
				new UnitActiveRole { UnitId = 1, UserId = "jo", Role = "Driver" });

			PersonnelMapLabel.Format("Shawn Jackson", "shawn", names).Should().Be("Shawn Jackson (Engine 1)");
			PersonnelMapLabel.Format("Jo Lee", "jo", names).Should().Be("Jo Lee (Engine 1, Medic 2)");
		}

		[Test]
		public async Task User_ids_match_without_regard_to_case()
		{
			var names = await Build(new UnitActiveRole { UnitId = 2, UserId = "ABC-123", Role = "Medic" });

			PersonnelMapLabel.Format("Sam Reed", "abc-123", names).Should().Be("Sam Reed (Medic 2)");
		}

		[Test]
		public async Task The_viewer_check_runs_once_per_seated_unit()
		{
			var asked = new List<int>();

			await PersonnelMapLabel.BuildUnitNamesByUserAsync(new[]
			{
				new UnitActiveRole { UnitId = 1, UserId = "a", Role = "Driver" },
				new UnitActiveRole { UnitId = 1, UserId = "b", Role = "Officer" }
			}, Units, unitId => { asked.Add(unitId); return Task.FromResult(true); });

			asked.Should().Equal(1);
		}

		[Test]
		public async Task Missing_inputs_leave_names_unchanged()
		{
			var names = await PersonnelMapLabel.BuildUnitNamesByUserAsync(null, null, CanView);

			names.Should().BeEmpty();
			PersonnelMapLabel.Format("Shawn Jackson", "shawn", null).Should().Be("Shawn Jackson");
			PersonnelMapLabel.Format("", "shawn", new Dictionary<string, string> { ["shawn"] = "Engine 1" }).Should().Be("");
		}
	}
}
