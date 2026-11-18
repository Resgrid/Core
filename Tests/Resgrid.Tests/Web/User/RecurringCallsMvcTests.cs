using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Dispatch;

namespace Resgrid.Tests.Web.User
{
	/// <summary>The Recurring Calls form (M0319): what it saves, the department's New Call Form Fields policy, and how a schedule reads.</summary>
	[TestFixture]
	public class RecurringCallsMvcTests
	{
		private static IStringLocalizer Localizer()
		{
			var strings = new Dictionary<string, string>
			{
				["SummaryWeekly"] = "Weekly on {0}",
				["SummaryWithTimes"] = "{0} at {1}",
				["SummaryMonthlyWeekday"] = "Monthly on the {0} {1}",
				["WeekLast"] = "last",
				["LeadTimeDay"] = "1 day before",
				["LeadTimeHours"] = "{0} hours before",
				["LeadTimeDays"] = "{0} days before"
			};
			var localizer = new Mock<IStringLocalizer>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, strings.TryGetValue(key, out var value) ? value : key));
			return localizer.Object;
		}

		private static RecurringCallEditView Form()
		{
			return new RecurringCallEditView
			{
				Name = "  IFT Mercy to Regional ",
				Priority = 2,
				Type = "Transfer",
				Frequency = (int)RecurringCallFrequencies.Weekly,
				RepeatInterval = 1,
				Days = new List<int> { 2, 4 },
				Times = new List<string> { "19:00", "08:00", "13:00", "" },
				StartsOn = "2026-11-17",
				LeadTimeMinutes = 1440,
				IsActive = true,
				Address = "1 Main St",
				Notes = "Bring the bariatric stretcher",
				ContactNumber = "555-0100",
				DispatchUnitIds = new List<int> { 11, 11, 0 },
				DispatchUserIds = new List<string> { "medic-1", " ", "MEDIC-1" }
			};
		}

		[Test]
		public void The_form_saves_the_customer_example_schedule()
		{
			var series = new RecurringCall();

			RecurringCallsController.ApplySchedule(Form(), series);
			RecurringCallsController.ApplyCallDetails(Form(), series, null);

			series.Frequency.Should().Be((int)RecurringCallFrequencies.Weekly);
			series.DaysOfWeek.Should().Be((int)(RecurringCallDays.Tuesday | RecurringCallDays.Thursday));
			series.Times.Should().Be("08:00,13:00,19:00");
			series.StartsOn.Should().Be(new DateTime(2026, 11, 17));
			series.EndsOn.Should().BeNull();
			series.Name.Should().Be("IFT Mercy to Regional");
			series.DispatchUnitIds.Should().Be("11");
			series.DispatchUserIds.Should().Be("medic-1");
			RecurringCallRecurrence.Validate(series).Should().BeEmpty();
		}

		[Test]
		public void A_field_the_department_hides_keeps_its_stored_value_on_edit()
		{
			var policy = new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.Note, Visible = false } } };
			var existing = new RecurringCall { RecurringCallId = 5, Notes = "Stored note", ContactNumber = "old" };
			var form = Form();
			form.Notes = null;

			RecurringCallsController.ApplyCallDetails(form, existing, policy);

			existing.Notes.Should().Be("Stored note", "the hidden field was not on the form");
			existing.ContactNumber.Should().Be("555-0100");
		}

		[Test]
		public void A_field_the_department_hides_is_empty_on_a_new_schedule()
		{
			var policy = new NewCallFieldPolicy { Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.Note, Visible = false } } };
			var series = new RecurringCall();

			RecurringCallsController.ApplyCallDetails(Form(), series, policy);

			series.Notes.Should().BeNull();
		}

		[Test]
		public void The_department_policy_requires_what_the_form_collects_and_skips_what_it_cannot()
		{
			var policy = new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule>
				{
					new NewCallFieldRule { Key = NewCallFieldKeys.ReferenceId, Visible = true, Required = true },
					new NewCallFieldRule { Key = NewCallFieldKeys.Protocols, Visible = true, Required = true },
					new NewCallFieldRule { Key = NewCallFieldKeys.DispatchOn, Visible = true, Required = true }
				}
			};
			var series = new RecurringCall();
			RecurringCallsController.ApplySchedule(Form(), series);
			RecurringCallsController.ApplyCallDetails(Form(), series, policy);

			var violations = NewCallFieldPolicyValidator.Validate(policy, RecurringCallsController.FieldValues(series));

			// Protocols cannot be picked on this form and every call it creates has a dispatch time.
			violations.Select(x => x.Key).Should().Equal(NewCallFieldKeys.ReferenceId);
		}

		[TestCase("39.7392,-104.9903", "39.7392,-104.9903")]
		[TestCase(" 39.7392 , -104.9903 ", "39.7392,-104.9903")]
		[TestCase("91,0", null)]
		[TestCase("abc", null)]
		[TestCase("1,2,3", null)]
		public void Coordinates_are_latitude_comma_longitude_in_range(string input, string expected)
		{
			RecurringCallsController.ParseCoordinates(input).Should().Be(expected);
		}

		[Test]
		public void A_schedule_reads_as_a_sentence()
		{
			var previous = CultureInfo.CurrentUICulture;
			CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
			try
			{
				var series = new RecurringCall();
				RecurringCallsController.ApplySchedule(Form(), series);
				RecurringCallsController.Describe(series, Localizer()).Should().Be("Weekly on Tuesday, Thursday at 08:00, 13:00, 19:00");

				series.Frequency = (int)RecurringCallFrequencies.Monthly;
				series.MonthlyWeek = 5;
				series.MonthlyWeekday = (int)DayOfWeek.Friday;
				series.Times = "09:00";
				RecurringCallsController.Describe(series, Localizer()).Should().Be("Monthly on the last Friday at 09:00");
			}
			finally
			{
				CultureInfo.CurrentUICulture = previous;
			}
		}

		[Test]
		public void Lead_times_read_in_hours_and_days()
		{
			RecurringCallsController.DescribeLeadTime(1440, Localizer()).Should().Be("1 day before");
			RecurringCallsController.DescribeLeadTime(2880, Localizer()).Should().Be("2 days before");
			RecurringCallsController.DescribeLeadTime(240, Localizer()).Should().Be("4 hours before");
		}
	}
}
