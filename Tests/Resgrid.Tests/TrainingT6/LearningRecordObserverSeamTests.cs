using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT6
{
	/// <summary>
	/// Round 9A: the baseline seam the LMS certification engine listens on. The writer tells its observers about records that now count and
	/// records voided (backfilled ones never), after the write is stored; an observer's failure never undoes the write; the LMS observer acts on
	/// voids whatever the add-on, and evaluates requirement sets only when the department has one and the add-on is active.
	/// </summary>
	[TestFixture]
	public class LearningRecordObserverSeamTests
	{
		private sealed class Recorder : ILearningRecordObserver
		{
			public readonly List<LearningRecordChange> Changes = new List<LearningRecordChange>();
			public bool Throw;

			public Task OnLearningRecordsChangedAsync(LearningRecordChange change, CancellationToken cancellationToken = default)
			{
				Changes.Add(change);
				if (Throw)
					throw new InvalidOperationException("observer failure");
				return Task.CompletedTask;
			}
		}

		private static LearningRecordDraft Draft(string user, bool backfill = false) => new LearningRecordDraft
		{
			UserId = user, Title = "Ladder drill", CompletedOn = DateTime.UtcNow.AddHours(-1), Hours = 2m, Status = LearningRecordStatuses.Verified, IsBackfill = backfill
		};

		[Test]
		public async Task The_writer_tells_observers_what_now_counts_and_what_was_voided_but_never_backfill()
		{
			var world = new LmsWorld();
			var recorder = new Recorder();
			world.Observers.Add(recorder);

			await world.Writer.WriteSourceAsync(LmsWorld.DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:5", new[] { Draft(LmsWorld.Member), Draft(LmsWorld.Member2) }, "officer");
			recorder.Changes.Should().ContainSingle();
			recorder.Changes[0].Written.Select(r => r.UserId).Should().BeEquivalentTo(new[] { LmsWorld.Member, LmsWorld.Member2 });
			recorder.Changes[0].Voided.Should().BeEmpty();
			recorder.Changes[0].ActingUserId.Should().Be("officer");

			// Member2 removed from the log: their record is voided and told; the unchanged one is not told again.
			await world.Writer.WriteSourceAsync(LmsWorld.DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:5", new[] { Draft(LmsWorld.Member) }, "officer");
			recorder.Changes.Should().HaveCount(2);
			recorder.Changes[1].Written.Should().BeEmpty();
			recorder.Changes[1].Voided.Should().ContainSingle().Which.UserId.Should().Be(LmsWorld.Member2);

			await world.Writer.VoidSourceAsync(LmsWorld.DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:5", "log_deleted", "officer");
			recorder.Changes.Last().Voided.Should().ContainSingle().Which.UserId.Should().Be(LmsWorld.Member);

			var before = recorder.Changes.Count;
			await world.Writer.WriteSourceAsync(LmsWorld.DepartmentId, LearningRecordSourceKinds.Import, "import:1", new[] { Draft(LmsWorld.Member, backfill: true) }, "officer");
			recorder.Changes.Should().HaveCount(before, "a backfilled history record is never news to the certification engine");
		}

		[Test]
		public async Task An_observer_failure_never_undoes_the_write()
		{
			var world = new LmsWorld();
			world.Observers.Add(new Recorder { Throw = true });

			var result = await world.Writer.WriteSourceAsync(LmsWorld.DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:9", new[] { Draft(LmsWorld.Member) }, "officer");

			result.Written.Should().Be(1);
			world.Records.Live.Should().ContainSingle();
		}

		[Test]
		public async Task The_LMS_observer_evaluates_sets_only_with_an_active_set_and_add_on_and_always_handles_voids()
		{
			var issuance = new Mock<ILmsCertificationIssuanceService>();
			var requirements = new Mock<ILmsCertificationRequirementService>();
			var sets = new Mock<ILmsCertificationRequirementSetsRepository>();
			var gate = new Mock<ILmsGateService>();
			var observer = new LmsCertificationLearningRecordObserver(issuance.Object, requirements.Object, sets.Object, gate.Object);
			var written = new LearningRecord { LearningRecordId = 1, DepartmentId = 3, UserId = "m", Status = (int)LearningRecordStatuses.Verified };
			var voided = new LearningRecord { LearningRecordId = 2, DepartmentId = 3, UserId = "m", Status = (int)LearningRecordStatuses.Voided };
			var external = new LearningRecord { LearningRecordId = 3, DepartmentId = 3, UserId = LmsExternalLearnerIds.New(), Status = (int)LearningRecordStatuses.Voided };

			await observer.OnLearningRecordsChangedAsync(new LearningRecordChange { DepartmentId = 3, ActingUserId = "o", Written = { written }, Voided = { voided, external } });
			issuance.Verify(i => i.OnRecordsVoidedAsync(3, "m", It.Is<IReadOnlyCollection<int>>(ids => ids.Single() == 2), "o", It.IsAny<CancellationToken>()), Times.Once);
			issuance.Verify(i => i.OnRecordsVoidedAsync(3, It.Is<string>(u => u.StartsWith("xl-")), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			gate.Verify(g => g.GetGateAsync(It.IsAny<int>()), Times.Never, "no active set: no billing call");
			requirements.Verify(r => r.EvaluateMembersAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

			sets.Setup(s => s.HasActiveSetsAsync(3)).ReturnsAsync(true);
			gate.Setup(g => g.GetGateAsync(3)).ReturnsAsync(new LmsDepartmentGate { DepartmentId = 3, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow });
			await observer.OnLearningRecordsChangedAsync(new LearningRecordChange { DepartmentId = 3, Written = { written } });
			requirements.Verify(r => r.EvaluateMembersAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never,
				"lapsed: the engine pauses (lapse rule 5)");

			gate.Setup(g => g.GetGateAsync(3)).ReturnsAsync(new LmsDepartmentGate { DepartmentId = 3, ModuleOn = true, FlagOn = true, AddonActive = true });
			await observer.OnLearningRecordsChangedAsync(new LearningRecordChange { DepartmentId = 3, ActingUserId = "o", Written = { written } });
			requirements.Verify(r => r.EvaluateMembersAsync(3, It.Is<IReadOnlyCollection<string>>(u => u.Single() == "m"), "o", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
