using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Workers
{
	/// <summary>
	/// The sweep that drains the legacy member-profile relocation backlog (ADP plan 5.1) — the part
	/// M0134 could not do in SQL because writing plaintext into an enrolled department's row would
	/// poison it.
	/// </summary>
	[TestFixture]
	public class MemberProfileRelocationLogicTests
	{
		private Mock<IMemberProfileRelocationService> _relocationService;
		private Mock<IDepartmentDataProtectionService> _protectionService;
		private List<int> _relocated;
		private MemberProfileRelocationLogic _logic;

		[SetUp]
		public void SetUp()
		{
			_relocated = new List<int>();
			_relocationService = new Mock<IMemberProfileRelocationService>();
			_relocationService.Setup(x => x.RelocateDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.Returns<int, CancellationToken>((d, ct) =>
				{
					_relocated.Add(d);
					return Task.FromResult(new MemberProfileRelocationResult { DepartmentId = d, MembersExamined = 1 });
				});

			_protectionService = new Mock<IDepartmentDataProtectionService>();
			_protectionService.Setup(x => x.GetStateAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync(DepartmentDataProtectionState.Disabled);

			_logic = new MemberProfileRelocationLogic(_relocationService.Object, _protectionService.Object);
		}

		private void SetupBacklog(params int[] departmentIds) =>
			_relocationService.Setup(x => x.GetDepartmentIdsWithOutstandingDataAsync())
				.ReturnsAsync(departmentIds);

		[Test]
		public async Task An_empty_backlog_is_a_cheap_no_op()
		{
			SetupBacklog();

			var result = await _logic.Process(CancellationToken.None);

			result.Item1.Should().BeTrue();
			result.Item2.Should().Contain("no outstanding");
			_relocationService.Verify(x => x.RelocateDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task Plaintext_and_fully_enrolled_departments_are_both_relocated()
		{
			// Disabled moves plaintext; Enabled moves through the ADP write path, which envelopes the
			// value as it lands. Both are steady states, so neither can race a migration cursor.
			SetupBacklog(1, 2);
			_protectionService.Setup(x => x.GetStateAsync(2, It.IsAny<bool>()))
				.ReturnsAsync(DepartmentDataProtectionState.Enabled);

			var result = await _logic.Process(CancellationToken.None);

			result.Item1.Should().BeTrue();
			_relocated.Should().Equal(1, 2);
		}

		[Test]
		public async Task A_department_mid_migration_is_left_to_its_encryption_night()
		{
			SetupBacklog(1);
			_protectionService.Setup(x => x.GetStateAsync(1, It.IsAny<bool>()))
				.ReturnsAsync(DepartmentDataProtectionState.Encrypting);

			var result = await _logic.Process(CancellationToken.None);

			_relocated.Should().BeEmpty();
			result.Item2.Should().Contain("deferred");
		}

		[Test]
		public async Task A_large_backlog_is_capped_and_says_so()
		{
			// A silent cap would read as "the backlog is empty" when it is not.
			var backlog = new List<int>();
			for (var i = 1; i <= 30; i++)
				backlog.Add(i);
			SetupBacklog(backlog.ToArray());

			var result = await _logic.Process(CancellationToken.None);

			_relocated.Should().HaveCount(25);
			result.Item2.Should().Contain("5 department(s) deferred");
		}

		[Test]
		public async Task A_failing_department_does_not_fail_the_sweep()
		{
			SetupBacklog(1, 2);
			_relocationService.Setup(x => x.RelocateDepartmentAsync(1, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("boom"));

			var result = await _logic.Process(CancellationToken.None);

			// The pass reports failure so the job surfaces it, and the untouched departments are
			// picked up on the next run — nothing was marked, so nothing was lost.
			result.Item1.Should().BeFalse();
		}
	}
}
