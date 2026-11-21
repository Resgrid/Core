using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Tasks;

namespace Resgrid.Tests.Workers.Console
{
	/// <summary>Worker 80: retention purges, then the dedicated number lifecycle, each in its own scope so one failing never stops the other.</summary>
	[TestFixture]
	public class IntakeMaintenanceTaskTests
	{
		private static Func<ILifetimeScope> Scopes(IIntakeService intake, IDepartmentPhoneNumbersService numbers)
		{
			var builder = new ContainerBuilder();
			builder.RegisterInstance(intake).As<IIntakeService>();
			builder.RegisterInstance(numbers).As<IDepartmentPhoneNumbersService>();
			var container = builder.Build();
			return () => container.BeginLifetimeScope();
		}

		[Test]
		public async Task purges_then_runs_the_number_lifecycle()
		{
			var now = new DateTime(2026, 11, 19, 3, 0, 0, DateTimeKind.Utc);
			var intake = new Mock<IIntakeService>();
			intake.Setup(i => i.PurgeExpiredAsync(now, It.IsAny<CancellationToken>())).ReturnsAsync(new IntakePurgeResult { Messages = 3, VoiceSessions = 1 });
			var numbers = new Mock<IDepartmentPhoneNumbersService>();
			numbers.Setup(n => n.ProcessLifecycleAsync(now, It.IsAny<CancellationToken>())).ReturnsAsync(2);

			await IntakeMaintenanceTask.RunOnceAsync(Scopes(intake.Object, numbers.Object), null, now, CancellationToken.None);

			intake.Verify(i => i.PurgeExpiredAsync(now, It.IsAny<CancellationToken>()), Times.Once);
			numbers.Verify(n => n.ProcessLifecycleAsync(now, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_failed_purge_still_runs_the_number_lifecycle()
		{
			var intake = new Mock<IIntakeService>();
			intake.Setup(i => i.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
			var numbers = new Mock<IDepartmentPhoneNumbersService>();

			Func<Task> run = () => IntakeMaintenanceTask.RunOnceAsync(Scopes(intake.Object, numbers.Object), null, DateTime.UtcNow, CancellationToken.None);

			await run.Should().NotThrowAsync();
			numbers.Verify(n => n.ProcessLifecycleAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_failed_lifecycle_does_not_fail_the_job()
		{
			var intake = new Mock<IIntakeService>();
			intake.Setup(i => i.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new IntakePurgeResult());
			var numbers = new Mock<IDepartmentPhoneNumbersService>();
			numbers.Setup(n => n.ProcessLifecycleAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("carrier"));

			Func<Task> run = () => IntakeMaintenanceTask.RunOnceAsync(Scopes(intake.Object, numbers.Object), null, DateTime.UtcNow, CancellationToken.None);

			await run.Should().NotThrowAsync();
		}
	}
}
