using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Support case 2026-11-08: a department renamed its unit types (MUG to "MUG 112", Ziekenwagen to "ZW 112") while
	/// changing their icons. Units name their type rather than point at it, so every unit was left without a type: no
	/// status set (an empty Set Status list), no icon on the Units page or the map, the apps fell back to the standard
	/// statuses and run cards matched no units. Saving a renamed type now moves the department's units to the new name.
	/// </summary>
	[TestFixture]
	public class UnitTypeRenameTests
	{
		private const int Dept = 13259;
		private const int MugTypeId = 8503;

		private Mock<IUnitTypesRepository> _types;
		private Mock<IUnitsRepository> _units;
		private Mock<IEventAggregator> _events;
		private Mock<ISearchProjectionService> _projections;
		private List<UnitType> _departmentTypes;
		private UnitsService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentTypes = new List<UnitType>
			{
				new UnitType { UnitTypeId = MugTypeId, DepartmentId = Dept, Type = "MUG" },
				new UnitType { UnitTypeId = 8504, DepartmentId = Dept, Type = "Ziekenwagen" }
			};

			_types = new Mock<IUnitTypesRepository>();
			_types.Setup(x => x.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((object id) => Copy(_departmentTypes.Find(t => t.UnitTypeId == (int)id)));
			_types.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(() => _departmentTypes);
			_types.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UnitType>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UnitType t, CancellationToken ct, bool firstLevelOnly) => t);

			_units = new Mock<IUnitsRepository>();
			_units.Setup(x => x.RenameUnitTypeForDepartmentAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
			_units.Setup(x => x.GetAllUnitsForTypeAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string type) => new List<Unit>
			{
				new Unit { UnitId = 14016, DepartmentId = Dept, Name = "MUG Zottegem", Type = type },
				new Unit { UnitId = 14125, DepartmentId = Dept, Name = "MUG Gent 1", Type = type }
			});

			_events = new Mock<IEventAggregator>();
			_projections = new Mock<ISearchProjectionService>();

			_service = new UnitsService(_units.Object, Mock.Of<IUnitStatesRepository>(), Mock.Of<IUnitLogsRepository>(),
				_types.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IUnitRolesRepository>(),
				Mock.Of<IUnitStateRoleRepository>(), Mock.Of<IUserStateService>(), _events.Object, Mock.Of<ICustomStateService>(),
				new Lazy<IMongoRepository<UnitsLocation>>(() => Mock.Of<IMongoRepository<UnitsLocation>>()),
				Mock.Of<IUnitLocationsDocRepository>(), new Lazy<IUnitLocationsMongoRepository>(() => Mock.Of<IUnitLocationsMongoRepository>()),
				Mock.Of<IUnitActiveRolesRepository>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ILimitsService>(), Mock.Of<IPersonnelRolesService>(),
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()),
				new Lazy<IRecordsCutoverService>(() => Mock.Of<IRecordsCutoverService>()), Mock.Of<ICallStatusAttributionService>(),
				searchProjections: new Lazy<ISearchProjectionService>(() => _projections.Object));
		}

		private static UnitType Copy(UnitType type) => type == null ? null : new UnitType
		{
			UnitTypeId = type.UnitTypeId,
			DepartmentId = type.DepartmentId,
			Type = type.Type,
			CustomStatesId = type.CustomStatesId,
			IconKey = type.IconKey
		};

		/// <summary>The page loads the stored type, changes it and saves it, like EditUnitType does.</summary>
		private async Task SaveRenamed(int unitTypeId, string newName)
		{
			var type = await _service.GetUnitTypeByIdAsync(unitTypeId);
			type.Type = newName;
			await _service.SaveUnitTypeAsync(type, CancellationToken.None);
		}

		private void VerifyNoUnitsMoved() =>
			_units.Verify(x => x.RenameUnitTypeForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task renaming_a_type_moves_its_units_to_the_new_name()
		{
			await SaveRenamed(MugTypeId, "MUG 112");

			_units.Verify(x => x.RenameUnitTypeForDepartmentAsync(Dept, "MUG", "MUG 112", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task the_moved_units_reach_the_apps_and_search()
		{
			await SaveRenamed(MugTypeId, "MUG 112");

			_events.Verify(x => x.SendMessage(It.Is<DepartmentSettingsUpdateEvent>(e => e.DepartmentId == Dept)), Times.Once);
			_projections.Verify(x => x.ProjectUnitAsync(It.Is<Unit>(u => u.Type == "MUG 112"), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task a_case_only_rename_still_moves_the_units()
		{
			// The map icon matches the type name exactly, so "mug" units would lose their pin under a "MUG" type.
			_departmentTypes[0].Type = "mug";

			await SaveRenamed(MugTypeId, "MUG");

			_units.Verify(x => x.RenameUnitTypeForDepartmentAsync(Dept, "mug", "MUG", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task saving_a_type_without_renaming_it_leaves_the_units_alone()
		{
			var type = await _service.GetUnitTypeByIdAsync(MugTypeId);
			type.CustomStatesId = 4540;
			type.IconKey = "208d2650228e4a5c83a0f08a66c449a8";

			await _service.SaveUnitTypeAsync(type, CancellationToken.None);

			VerifyNoUnitsMoved();
			_events.Verify(x => x.SendMessage(It.IsAny<DepartmentSettingsUpdateEvent>()), Times.Never);
		}

		[Test]
		public async Task a_new_type_moves_no_units()
		{
			await _service.SaveUnitTypeAsync(new UnitType { DepartmentId = Dept, Type = "PIT 112" }, CancellationToken.None);

			VerifyNoUnitsMoved();
			_types.Verify(x => x.GetByIdAsync(It.IsAny<object>()), Times.Never);
		}

		[Test]
		public async Task units_are_not_moved_when_another_type_still_answers_to_the_old_name()
		{
			// Two types whose names differ only by case: the units named "MUG" may be the other type's.
			_departmentTypes.Add(new UnitType { UnitTypeId = 9999, DepartmentId = Dept, Type = "mug" });

			await SaveRenamed(MugTypeId, "MUG 112");

			VerifyNoUnitsMoved();
		}

		[Test]
		public async Task a_type_saved_under_another_department_moves_nothing()
		{
			var type = await _service.GetUnitTypeByIdAsync(MugTypeId);
			type.DepartmentId = 1;
			type.Type = "MUG 112";

			await _service.SaveUnitTypeAsync(type, CancellationToken.None);

			VerifyNoUnitsMoved();
		}

		[Test]
		public async Task a_rename_that_moves_no_units_raises_no_refresh()
		{
			_units.Setup(x => x.RenameUnitTypeForDepartmentAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

			await SaveRenamed(MugTypeId, "MUG 112");

			_events.Verify(x => x.SendMessage(It.IsAny<DepartmentSettingsUpdateEvent>()), Times.Never);
			_projections.Verify(x => x.ProjectUnitAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
