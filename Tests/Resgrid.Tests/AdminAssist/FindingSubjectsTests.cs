using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>The empty-groups finding names its groups on attended pages; evidence and everything downstream stay scalar.</summary>
	[TestFixture]
	public class FindingSubjectsTests
	{
		private static readonly AdminAssistActor Actor = new(7, "admin");
		private Mock<IDepartmentGroupsRepository> _groups;
		private Mock<IDepartmentsService> _departments;
		private Mock<IRecordsAuthorizationService> _membership;
		private OrganizationFindingSubjects _subjects;

		private static DepartmentGroup Group(int id, string name, params string[] members) => new()
		{
			DepartmentGroupId = id, DepartmentId = 7, Name = name,
			Members = members.Select(m => new DepartmentGroupMember { DepartmentGroupId = id, UserId = m }).ToList()
		};

		[SetUp]
		public void SetUp()
		{
			_groups = new(); _departments = new(); _membership = new();
			_groups.Setup(g => g.GetAllGroupsByDepartmentIdAsync(7)).ReturnsAsync(new[]
			{
				Group(1, "Station 1", "active"), Group(2, "Training", "hidden"), Group(3, "Hazmat"), Group(4, "  "), Group(5, "Admin team", "hidden", "active")
			});
			_departments.Setup(d => d.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(7, true))
				.ReturnsAsync(new List<IdentityUser> { new() { Id = "active" }, new() { Id = "hidden" } });
			// The evidence's test: only assignable members make a group active (hidden, disabled and removed members do not).
			_membership.Setup(m => m.GetAssignableMemberIdsAsync(It.IsAny<IEnumerable<string>>(), 7)).ReturnsAsync(new HashSet<string> { "active" });
			_subjects = new OrganizationFindingSubjects(_groups.Object, _departments.Object, _membership.Object);
		}

		[Test]
		public async Task Empty_groups_are_named_alphabetically_with_an_id_when_unnamed()
		{
			var subjects = await _subjects.ReadAsync(Actor, "empty-groups", CancellationToken.None);

			Assert.That(subjects.Select(s => (s.Id, s.Name)), Is.EqualTo(new[] { ("4", "#4"), ("3", "Hazmat"), ("2", "Training") }));
		}

		[Test]
		public async Task Other_rules_read_nothing()
		{
			Assert.That(await _subjects.ReadAsync(Actor, "unit-type", CancellationToken.None), Is.Empty);
			_groups.VerifyNoOtherCalls(); _departments.VerifyNoOtherCalls(); _membership.VerifyNoOtherCalls();
		}

		[Test]
		public void A_group_outside_the_department_is_refused()
		{
			_groups.Setup(g => g.GetAllGroupsByDepartmentIdAsync(7)).ReturnsAsync(new[] { Group(1, "Station 1"), new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 8, Name = "Other", Members = new List<DepartmentGroupMember>() } });

			Assert.ThrowsAsync<InvalidOperationException>(async () => await _subjects.ReadAsync(Actor, "empty-groups", CancellationToken.None));
		}

		private static ConfigurationReport Report(params (string RuleId, RuleResult Result)[] findings)
		{
			var snapshot = new ConfigurationSnapshot(7, "admin", "1", DateTime.UtcNow, true, new Dictionary<string, ConfigurationEvidence>());
			return new ConfigurationReport(snapshot, findings.Select(f => new ConfigurationFinding(f.RuleId, "people", FindingSeverity.Warning, f.Result,
				"Rule." + f.RuleId, "RuleWhy." + f.RuleId, "RuleNext." + f.RuleId, "/User/Groups/Index", Array.Empty<string>(), "1", DateTime.UtcNow)).ToArray(), new[] { "people" });
		}

		private static (AdminAssistService Service, Mock<IAdminAssistAccessService> Access, Mock<IAdminAssistFindingSubjectSource> Source) Service()
		{
			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(Actor, true, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var source = new Mock<IAdminAssistFindingSubjectSource>();
			source.SetupGet(s => s.RuleIds).Returns(new[] { "empty-groups" });
			source.Setup(s => s.ReadAsync(Actor, "empty-groups", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new FindingSubject("3", "Hazmat") });
			var service = new AdminAssistService(access.Object, new ConfigurationCatalog(), Mock.Of<IConfigurationSnapshotProvider>(), Mock.Of<IAdminAssistRepository>(),
				TimeProvider.System, new[] { source.Object });
			return (service, access, source);
		}

		[Test]
		public async Task Only_a_failing_finding_is_named()
		{
			var (service, _, source) = Service();

			var failing = await service.GetFindingSubjectsAsync(Actor, true, Report(("empty-groups", RuleResult.Fail), ("unit-type", RuleResult.Fail)));
			Assert.That(failing.Keys, Is.EqualTo(new[] { "empty-groups" }));
			Assert.That(failing["empty-groups"].Single().Name, Is.EqualTo("Hazmat"));

			foreach (var result in new[] { RuleResult.Pass, RuleResult.Unknown, RuleResult.NotApplicable })
				Assert.That(await service.GetFindingSubjectsAsync(Actor, true, Report(("empty-groups", result))), Is.Empty, result.ToString());
			source.Verify(s => s.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void Names_are_not_read_without_access()
		{
			var (service, access, source) = Service();
			access.Setup(a => a.CanAccessAsync(Actor, true, It.IsAny<CancellationToken>())).ReturnsAsync(false);

			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetFindingSubjectsAsync(Actor, true, Report(("empty-groups", RuleResult.Fail))));
			source.Verify(s => s.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_failed_name_read_leaves_the_finding_without_names()
		{
			var (service, _, source) = Service();
			source.Setup(s => s.ReadAsync(Actor, "empty-groups", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Scope or row bound exceeded."));

			Assert.That(await service.GetFindingSubjectsAsync(Actor, true, Report(("empty-groups", RuleResult.Fail))), Is.Empty);
		}

		[Test]
		public async Task A_stalled_name_read_is_bounded_and_leaves_the_finding_without_names()
		{
			var saved = Resgrid.Config.AdminAssistConfig.EvidenceSourceTimeoutSeconds;
			Resgrid.Config.AdminAssistConfig.EvidenceSourceTimeoutSeconds = 1;
			try
			{
				var (service, access, source) = Service();
				// Never completes: a group or member lookup that hangs while the request stays open.
				source.Setup(s => s.ReadAsync(Actor, "empty-groups", It.IsAny<CancellationToken>())).Returns(new TaskCompletionSource<IReadOnlyList<FindingSubject>>().Task);

				var read = service.GetFindingSubjectsAsync(Actor, true, Report(("empty-groups", RuleResult.Fail)));
				Assert.That(await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(read), "The name read has its own deadline.");
				Assert.That(await read, Is.Empty);
				// The closing access check still runs on the caller's token, not the spent name-read bound.
				access.Verify(a => a.CanAccessAsync(Actor, true, It.Is<CancellationToken>(t => !t.IsCancellationRequested)), Times.Exactly(2));
			}
			finally { Resgrid.Config.AdminAssistConfig.EvidenceSourceTimeoutSeconds = saved; }
		}

		[Test]
		public void Caller_cancellation_is_not_swallowed()
		{
			var (service, _, source) = Service();
			using var cancellation = new CancellationTokenSource();
			source.Setup(s => s.ReadAsync(Actor, "empty-groups", It.IsAny<CancellationToken>())).Returns(async () => { cancellation.Cancel(); await Task.Yield(); cancellation.Token.ThrowIfCancellationRequested(); return Array.Empty<FindingSubject>(); });

			Assert.CatchAsync<OperationCanceledException>(async () => await service.GetFindingSubjectsAsync(Actor, true, Report(("empty-groups", RuleResult.Fail)), cancellation.Token));
		}
	}
}
