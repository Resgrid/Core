using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class BuiltInStatusBaseTypesTests
	{
		[Test]
		public void every_built_in_unit_status_classifies_the_same_through_its_base_type()
		{
			foreach (UnitStateTypes state in Enum.GetValues(typeof(UnitStateTypes)))
			{
				AvailabilityMatrix.ForCustomBaseType((int)BuiltInStatusBaseTypes.ForUnitState((int)state))
					.Should().Be(AvailabilityMatrix.ForUnitStateType((int)state), state.ToString());
			}
		}

		[Test]
		public void every_built_in_personnel_status_but_on_unit_classifies_the_same_through_its_base_type()
		{
			foreach (ActionTypes status in Enum.GetValues(typeof(ActionTypes)))
			{
				if (status == ActionTypes.OnUnit)
					continue;

				AvailabilityMatrix.ForCustomBaseType((int)BuiltInStatusBaseTypes.ForPersonnelStatus((int)status))
					.Should().Be(AvailabilityMatrix.ForBuiltInPersonnelActionType((int)status), status.ToString());
			}
		}

		[Test]
		public void on_unit_has_no_meaning_of_its_own()
		{
			BuiltInStatusBaseTypes.ForPersonnelStatus((int)ActionTypes.OnUnit).Should().Be(ActionBaseTypes.None);
		}

		[Test]
		public void built_in_staffing_levels_are_their_own_base_types()
		{
			foreach (UserStateTypes staffing in Enum.GetValues(typeof(UserStateTypes)))
				((int)BuiltInStatusBaseTypes.ForStaffing((int)staffing)).Should().Be((int)staffing, staffing.ToString());
		}

		[TestCase(UserStateTypes.Available, AvailabilityClass.Available)]
		[TestCase(UserStateTypes.OnShift, AvailabilityClass.Available)]
		[TestCase(UserStateTypes.Delayed, AvailabilityClass.Delayed)]
		[TestCase(UserStateTypes.Unavailable, AvailabilityClass.Unavailable)]
		[TestCase(UserStateTypes.Committed, AvailabilityClass.Committed)]
		public void built_in_staffing_classifies_as_dispatch_has_always_treated_it(UserStateTypes staffing, AvailabilityClass expected)
		{
			AvailabilityMatrix.ForStaffingBaseType((int)BuiltInStatusBaseTypes.ForStaffing((int)staffing)).Should().Be(expected);
		}

		[Test]
		public void every_staffing_base_type_but_none_is_classified()
		{
			foreach (StaffingBaseTypes baseType in Enum.GetValues(typeof(StaffingBaseTypes)).Cast<StaffingBaseTypes>().Where(x => x != StaffingBaseTypes.None))
				AvailabilityMatrix.ForStaffingBaseType((int)baseType).Should().NotBe(AvailabilityClass.Unknown, baseType.ToString());
		}

		[Test]
		public void legacy_staffing_values_classify_as_before()
		{
			// Staffing options only ever held -1 (None) or 0, which the status matrix read as Available.
			AvailabilityMatrix.ForStaffingBaseType(-1).Should().Be(AvailabilityMatrix.ForCustomBaseType(-1));
			AvailabilityMatrix.ForStaffingBaseType(0).Should().Be(AvailabilityMatrix.ForCustomBaseType(0));
		}
	}

	[TestFixture]
	public class CallPriorityTagTests
	{
		[Test]
		public void built_in_priorities_keep_the_p_number_tag()
		{
			var call = new Call { Priority = (int)CallPriority.High };

			call.GetPriorityTag().Should().Be("P2");
			call.GetPriorityDisplayText().Should().Be("High");
		}

		[Test]
		public void a_department_priority_is_named_not_numbered()
		{
			var call = new Call { Priority = 57, CallPriority = new DepartmentCallPriority { DepartmentCallPriorityId = 57, Name = "Structure Fire" } };

			call.GetPriorityTag().Should().Be("Structure Fire");
			call.GetPriorityDisplayText().Should().Be("Structure Fire");
		}

		[Test]
		public void a_department_priority_with_an_id_up_to_3_is_named_too()
		{
			// The oldest departments own priority ids 0 to 3; their own priority is named, not "P2".
			var call = new Call { DepartmentId = 1, Priority = 2, CallPriority = new DepartmentCallPriority { DepartmentCallPriorityId = 2, DepartmentId = 1, Name = "Urgent" } };

			call.GetPriorityTag().Should().Be("Urgent");
			call.GetPriorityDisplayText().Should().Be("Urgent");
		}

		[Test]
		public void an_unresolved_department_priority_falls_back_to_its_id()
		{
			var call = new Call { Priority = 57 };

			call.GetPriorityTag().Should().Be("P57");
			call.GetPriorityDisplayText().Should().Be("57");
		}
	}
}
