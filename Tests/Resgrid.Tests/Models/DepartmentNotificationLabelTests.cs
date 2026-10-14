using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class DepartmentNotificationLabelTests
	{
		private static DepartmentNotification Rule(EventTypes type, string before, string current) =>
			new DepartmentNotification { EventType = (int)type, BeforeData = before, CurrentData = current };

		// A fake localizer: German words for the keys it knows, the English fallback otherwise.
		private static readonly Dictionary<string, string> German = new Dictionary<string, string>
		{
			["StatusRuleAny"] = "Beliebig",
			["AnyStatusWithMeaning"] = "Jeder Status „{0}“",
			["Responding"] = "Auf Anfahrt",
			["StaffingOffDuty"] = "Dienstfrei",
			["StatusRuleNone"] = "Keine"
		};

		private static string Label(string key, string fallback) => German.TryGetValue(key, out var text) ? text : fallback;

		[Test]
		public void without_a_localizer_the_labels_stay_english()
		{
			var rule = Rule(EventTypes.PersonnelStatusChanged, "-1", StatusMatch.BaseTypeToken((int)ActionBaseTypes.Responding));

			rule.TranslateBefore().Should().Be("Any");
			rule.TranslateCurrent().Should().Be("Any \"Responding\" status");
		}

		[Test]
		public void the_localizer_names_any_and_meanings()
		{
			var status = Rule(EventTypes.PersonnelStatusChanged, "-1", StatusMatch.BaseTypeToken((int)ActionBaseTypes.Responding));
			status.TranslateBefore(null, Label).Should().Be("Beliebig");
			status.TranslateCurrent(null, Label).Should().Be("Jeder Status „Auf Anfahrt“");

			var staffing = Rule(EventTypes.PersonnelStaffingChanged, "-1", StatusMatch.BaseTypeToken((int)StaffingBaseTypes.OffDuty));
			staffing.TranslateCurrent(null, Label).Should().Be("Jeder Status „Dienstfrei“");

			Rule(EventTypes.DocumentAdded, null, null).TranslateCurrent(null, Label).Should().Be("Keine");
		}

		[Test]
		public void a_department_option_keeps_its_own_name()
		{
			var states = new List<CustomState>
			{
				new CustomState { CustomStateId = 5, Type = (int)CustomStateTypes.Personnel, Details = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = 900, CustomStateId = 5, ButtonText = "Code 3" } } }
			};

			Rule(EventTypes.PersonnelStatusChanged, "-1", "900").TranslateCurrent(states, Label).Should().Be("Code 3");
		}
	}
}
