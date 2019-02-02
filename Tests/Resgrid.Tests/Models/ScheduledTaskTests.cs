using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class ScheduledTaskTests
	{
		[Test]
		public void TestWhenShouldJobBeRunPMtoPM()
		{
			var task = new ScheduledTask();
			task.ScheduleType = (int)ScheduleTypes.Weekly;
			task.Sunday = true;
			task.Monday = true;
			task.Tuesday = true;
			task.Wednesday = true;
			task.Thursday = true;
			task.Friday = true;
			task.Saturday = true;
			task.Time = "7:30 PM";

			var testingDateTime = new DateTime(2013, 7, 21, 21, 2, 44, DateTimeKind.Local);

			var dateTime = task.WhenShouldJobBeRun(testingDateTime);

			dateTime.HasValue.Should().BeTrue();
			dateTime.Value.Hour.Should().Be(19);
			dateTime.Value.Minute.Should().Be(30);
			dateTime.Value.Second.Should().Be(0);
			dateTime.Value.Day.Should().Be(21);
			dateTime.Value.Month.Should().Be(7);
			dateTime.Value.Year.Should().Be(2013);
		}

		[Test]
		public void TestWhenShouldJobBeRunAMtoAM()
		{
			var task = new ScheduledTask();
			task.ScheduleType = (int)ScheduleTypes.Weekly;
			task.Sunday = true;
			task.Monday = true;
			task.Tuesday = true;
			task.Wednesday = true;
			task.Thursday = true;
			task.Friday = true;
			task.Saturday = true;
			task.Time = "8:30 AM";

			var testingDateTime = new DateTime(2013, 7, 21, 6, 2, 44, DateTimeKind.Local);

			var dateTime = task.WhenShouldJobBeRun(testingDateTime);

			dateTime.HasValue.Should().BeTrue();
			dateTime.Value.Hour.Should().Be(8);
			dateTime.Value.Minute.Should().Be(30);
			dateTime.Value.Second.Should().Be(0);
			dateTime.Value.Day.Should().Be(21);
			dateTime.Value.Month.Should().Be(7);
			dateTime.Value.Year.Should().Be(2013);
		}

		[Test]
		public void TestWhenShouldJobBeRunAMtoPM()
		{
			var task = new ScheduledTask();
			task.ScheduleType = (int)ScheduleTypes.Weekly;
			task.Sunday = true;
			task.Monday = true;
			task.Tuesday = true;
			task.Wednesday = true;
			task.Thursday = true;
			task.Friday = true;
			task.Saturday = true;
			task.Time = "8:45 PM";

			var testingDateTime = new DateTime(2013, 7, 21, 6, 2, 1, DateTimeKind.Local);

			var dateTime = task.WhenShouldJobBeRun(testingDateTime);

			dateTime.HasValue.Should().BeTrue();
			dateTime.Value.Hour.Should().Be(20);
			dateTime.Value.Minute.Should().Be(45);
			dateTime.Value.Second.Should().Be(0);
			dateTime.Value.Day.Should().Be(21);
			dateTime.Value.Month.Should().Be(7);
			dateTime.Value.Year.Should().Be(2013);
		}

		[Test]
		public void TestWhenShouldJobBeRunPMtoAM()
		{
			var task = new ScheduledTask();
			task.ScheduleType = (int)ScheduleTypes.Weekly;
			task.Sunday = true;
			task.Monday = true;
			task.Tuesday = true;
			task.Wednesday = true;
			task.Thursday = true;
			task.Friday = true;
			task.Saturday = true;
			task.Time = "6:15 AM";

			var testingDateTime = new DateTime(2013, 7, 21, 19, 59, 59, DateTimeKind.Local);

			var dateTime = task.WhenShouldJobBeRun(testingDateTime);

			dateTime.HasValue.Should().BeTrue();
			dateTime.Value.Hour.Should().Be(6);
			dateTime.Value.Minute.Should().Be(15);
			dateTime.Value.Second.Should().Be(0);
			dateTime.Value.Day.Should().Be(21);
			dateTime.Value.Month.Should().Be(7);
			dateTime.Value.Year.Should().Be(2013);
		}
	}
}
