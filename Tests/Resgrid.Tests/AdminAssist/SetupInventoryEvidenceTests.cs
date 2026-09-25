using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class SetupInventoryEvidenceTests
	{
		[Test]
		public async Task Each_setup_count_is_read_independently_and_carries_no_content()
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallTypesForDepartmentAsync(7)).ReturnsAsync(new List<CallType> { new() { Type = "Structure Fire" }, new() { Type = "Medical" } });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForDepartmentAsync(7)).ThrowsAsync(new InvalidOperationException("private detail"));
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitTypesForDepartmentAsync(7)).ReturnsAsync(new List<UnitType>());
			var source = new SetupInventoryEvidenceSource(calls.Object, roles.Object, units.Object, Mock.Of<INotificationService>(),
				Mock.Of<IDistributionListsService>(), Mock.Of<ITrainingService>(), Mock.Of<ICertificationService>(), Mock.Of<IChecklistsService>(),
				Mock.Of<IWorkflowService>(), Mock.Of<IProtocolsService>());

			var evidence = (await source.ReadAsync(new AdminAssistActor(7, "admin"), DateTime.UtcNow, CancellationToken.None)).ToDictionary(e => e.Id);

			Assert.That(evidence.Keys, Is.EquivalentTo(source.EvidenceIds));
			Assert.That(evidence["callTypeCount"].Number, Is.EqualTo(2m));
			Assert.That(evidence["callTypeCount"].Code, Is.Null, "Counts only; no call type names leave the adapter.");
			Assert.That(evidence["personnelRoleCount"].State, Is.EqualTo(EvidenceState.Unknown), "A failed read is unknown, never zero.");
			Assert.That(evidence["personnelRoleCount"].ReasonCode, Is.EqualTo("SourceUnavailable"));
			Assert.That((evidence["unitTypeCount"].State, evidence["unitTypeCount"].Number), Is.EqualTo((EvidenceState.Known, (decimal?)0)), "Other counts are unaffected by one failure.");
			Assert.That(evidence["trainingCount"].State, Is.EqualTo(EvidenceState.Unknown), "A missing answer is unknown, not zero.");
		}
	}
}
