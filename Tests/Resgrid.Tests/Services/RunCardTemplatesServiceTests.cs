using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class RunCardTemplatesServiceTests
	{
		private const int DepartmentId = 4;

		private Mock<IRunCardsService> _runCards;
		private Mock<IResponsePackagesService> _packages;
		private Mock<IUnitsService> _units;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentSettingsService> _settings;
		private RunCardTemplatesService _service;
		private List<UnitType> _unitTypes;
		private List<ResponsePackage> _savedPackages;
		private RunCard _savedCard;
		private DispatchResponseCodesConfig _codes;

		[SetUp]
		public void SetUp()
		{
			_runCards = new Mock<IRunCardsService>();
			_packages = new Mock<IResponsePackagesService>();
			_units = new Mock<IUnitsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_calls = new Mock<ICallsService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_savedPackages = new List<ResponsePackage>();
			_codes = new DispatchResponseCodesConfig();

			_unitTypes = new List<UnitType>
			{
				new UnitType { UnitTypeId = 1, Type = "Engine", BaseType = (int)UnitTypeClasses.Engine },
				new UnitType { UnitTypeId = 2, Type = "Pumper", BaseType = (int)UnitTypeClasses.Engine },
				new UnitType { UnitTypeId = 3, Type = "Truck", BaseType = (int)UnitTypeClasses.None },
				new UnitType { UnitTypeId = 4, Type = "Quint", BaseType = (int)UnitTypeClasses.Aerial },
				new UnitType { UnitTypeId = 5, Type = "Battalion", BaseType = (int)UnitTypeClasses.Command },
				new UnitType { UnitTypeId = 6, Type = "Heavy Rescue", BaseType = (int)UnitTypeClasses.Rescue },
				new UnitType { UnitTypeId = 7, Type = "Utility", BaseType = (int)UnitTypeClasses.Support }
			};
			_units.Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _unitTypes);
			_roles.Setup(x => x.GetRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonnelRole>());
			_calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 21, Name = "Urgent", BaseType = (int)CallPriority.High },
				new DepartmentCallPriority { DepartmentCallPriorityId = 22, Name = "Routine", BaseType = (int)CallPriority.Low }
			});
			_calls.Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>
			{
				new CallType { CallTypeId = 31, Type = "Structure Fire", BaseType = (int)CallTypeClasses.Fire },
				new CallType { CallTypeId = 32, Type = "Lift Assist", BaseType = (int)CallTypeClasses.ServiceCall }
			});
			_packages.Setup(x => x.GetPackagesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<ResponsePackage>());
			_packages.Setup(x => x.SavePackageAsync(It.IsAny<ResponsePackage>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ResponsePackage p, string u, CancellationToken c) =>
				{
					p.ResponsePackageId = 100 + _savedPackages.Count;
					_savedPackages.Add(p);
					return p;
				});
			_runCards.Setup(x => x.GetAllRunCardsForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<RunCard>());
			_runCards.Setup(x => x.SaveRunCardAsync(It.IsAny<RunCard>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RunCard c, CancellationToken t) => { c.RunCardId = 77; _savedCard = c; return c; });
			_settings.Setup(x => x.GetDispatchResponseCodesAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _codes);

			_service = new RunCardTemplatesService(_runCards.Object, _packages.Object, _units.Object, _roles.Object, _calls.Object, _settings.Object);
		}

		[Test]
		public async Task preview_maps_classes_to_every_type_of_the_class_and_names_to_unclassed_types()
		{
			var preview = await _service.PreviewAsync(DepartmentId, "fire-structure-first-alarm");

			preview.Mappings.Should().Contain(m => m.StartsWith("Engine (unit class)") && m.Contains("Engine, Pumper"));
			// "Truck" has no class but carries a suggested name of the Aerial class; the Quint is Aerial too.
			preview.Mappings.Should().Contain(m => m.StartsWith("Aerial (unit class)") && m.Contains("Quint"));
			preview.Triggers.Should().ContainSingle().Which.Should().Be("Urgent + Structure Fire");
			preview.PackagesCreated.Should().Contain("Structure fire, first alarm");
			preview.CodesAdded.Should().Contain(c => c.StartsWith("Code 3"));
		}

		[Test]
		public async Task an_unmatched_resource_blocks_applying()
		{
			var preview = await _service.PreviewAsync(DepartmentId, "ems-closest-ambulance");

			preview.CanApply.Should().BeFalse();
			preview.Unmatched.Should().ContainSingle(u => u.Contains("Ambulance"));

			var result = await _service.ApplyAsync(DepartmentId, "ems-closest-ambulance", "admin");
			result.Success.Should().BeFalse();
			_runCards.Verify(x => x.SaveRunCardAsync(It.IsAny<RunCard>(), It.IsAny<CancellationToken>()), Times.Never);
			_packages.Verify(x => x.SavePackageAsync(It.IsAny<ResponsePackage>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task apply_creates_packages_codes_and_a_disabled_card()
		{
			var result = await _service.ApplyAsync(DepartmentId, "fire-structure-first-alarm", "admin");

			result.Success.Should().BeTrue();
			result.RunCardId.Should().Be(77);
			_savedCard.IsDisabled.Should().BeTrue();
			_savedCard.AlarmLevels.Should().HaveCount(3);
			_savedCard.AlarmLevels.Should().OnlyContain(l => l.ResponsePackageId.HasValue);
			_savedCard.Triggers.Should().ContainSingle(t => t.TriggerType == 2 && t.Priority == 21 && t.CallTypeId == 31);

			var first = _savedPackages.Single(p => p.Name == "Structure fire, first alarm");
			var ladder = first.Rows.Single(r => r.Label == "Ladder");
			ladder.KeepOutstanding.Should().BeTrue();
			ladder.SubstitutePackageId.Should().Be(_savedPackages.Single(p => p.Name == "Quint").ResponsePackageId);
			ladder.ResponseCodeId.Should().NotBeNull();
			first.Rows.Single(r => r.Label == "Engines").Targets.Should().ContainSingle(t => t.TargetKind == (int)RunCardTargetKinds.UnitClass && t.TargetRefId == (int)UnitTypeClasses.Engine);

			_codes.Codes.Should().Contain(c => c.Code == "Code 3");
			_settings.Verify(x => x.SetDispatchResponseCodesAsync(DepartmentId, It.IsAny<DispatchResponseCodesConfig>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task apply_builds_the_decision_graph_with_package_ids()
		{
			var result = await _service.ApplyAsync(DepartmentId, "fire-alarm-single-engine-upgrade", "admin");

			result.Success.Should().BeTrue();
			var nodes = _savedCard.AlarmLevels.Single().DecisionNodes;
			nodes.Should().HaveCount(3);
			RunCardDecisionGraph.Validate(nodes).Should().BeEmpty();
			nodes.Where(n => n.NodeKind == (int)RunCardDecisionNodeKinds.Outcome).Should().OnlyContain(n => n.ResponsePackageId.HasValue);
		}

		[Test]
		public async Task suggestions_favour_call_types_no_card_covers()
		{
			var suggestions = await _service.GetSuggestionsAsync(DepartmentId, 20);

			suggestions.Should().Contain(s => s.Template.Id == "fire-structure-first-alarm" && s.Reason.Contains("Structure Fire"));
			suggestions.Should().OnlyContain(s => s.Template.CallTypeClass == CallTypeClasses.Fire || s.Template.CallTypeClass == CallTypeClasses.ServiceCall);
		}
	}
}
