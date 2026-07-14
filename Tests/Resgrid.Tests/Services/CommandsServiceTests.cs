using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Covers <see cref="CommandsService"/>: command definition (pre-configured command board) templates.
	/// Focuses on lane (CommandDefinitionRole) hydration, call-type template resolution with the
	/// "Any Call Type" fallback, and the Save reconcile (insert/update/delete of lanes).
	/// </summary>
	[TestFixture]
	public class CommandsServiceTests
	{
		private const int Dept = 44;

		private Mock<ICommandDefinitionRepository> _definitions;
		private Mock<ICommandDefinitionRoleRepository> _roles;
		private Mock<ICommandDefinitionRoleUnitTypeRepository> _roleUnitTypes;
		private Mock<ICommandDefinitionRolePersonnelRoleRepository> _rolePersonnelRoles;
		private CommandsService _service;

		[SetUp]
		public void SetUp()
		{
			_definitions = new Mock<ICommandDefinitionRepository>();
			_roles = new Mock<ICommandDefinitionRoleRepository>();
			_roleUnitTypes = new Mock<ICommandDefinitionRoleUnitTypeRepository>();
			_rolePersonnelRoles = new Mock<ICommandDefinitionRolePersonnelRoleRepository>();

			_roleUnitTypes.Setup(x => x.GetUnitTypesByCommandDefinitionIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<CommandDefinitionRoleUnitType>());
			_roleUnitTypes.Setup(x => x.GetUnitTypesByRoleIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<CommandDefinitionRoleUnitType>());
			_rolePersonnelRoles.Setup(x => x.GetPersonnelRolesByCommandDefinitionIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<CommandDefinitionRolePersonnelRole>());
			_rolePersonnelRoles.Setup(x => x.GetPersonnelRolesByRoleIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<CommandDefinitionRolePersonnelRole>());

			_service = new CommandsService(_definitions.Object, _roles.Object, _roleUnitTypes.Object, _rolePersonnelRoles.Object, new MockUnitOfWork());
		}

		[Test]
		public async Task GetCommandForCallTypeAsync_MatchesCallType_AndHydratesLanes()
		{
			_definitions.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CommandDefinition>
			{
				new CommandDefinition { CommandDefinitionId = 1, DepartmentId = Dept, CallTypeId = null },
				new CommandDefinition { CommandDefinitionId = 2, DepartmentId = Dept, CallTypeId = 7 }
			});

			_roles.Setup(x => x.GetRolesByCommandDefinitionIdAsync(2)).ReturnsAsync(new List<CommandDefinitionRole>
			{
				new CommandDefinitionRole { CommandDefinitionRoleId = 20, CommandDefinitionId = 2, Name = "RIT", SortOrder = 1 },
				new CommandDefinitionRole { CommandDefinitionRoleId = 21, CommandDefinitionId = 2, Name = "Interior", SortOrder = 0 }
			});

			var result = await _service.GetCommandForCallTypeAsync(Dept, 7);

			result.Should().NotBeNull();
			result.CommandDefinitionId.Should().Be(2);
			result.Assignments.Should().HaveCount(2);
			result.Assignments.First().Name.Should().Be("Interior"); // ordered by SortOrder
		}

		[Test]
		public async Task GetCommandForCallTypeAsync_FallsBackToAnyCallTypeDefinition()
		{
			_definitions.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CommandDefinition>
			{
				new CommandDefinition { CommandDefinitionId = 1, DepartmentId = Dept, CallTypeId = null },
				new CommandDefinition { CommandDefinitionId = 2, DepartmentId = Dept, CallTypeId = 7 }
			});

			_roles.Setup(x => x.GetRolesByCommandDefinitionIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<CommandDefinitionRole>());

			var result = await _service.GetCommandForCallTypeAsync(Dept, 99);

			result.Should().NotBeNull();
			result.CommandDefinitionId.Should().Be(1);
		}

		[Test]
		public async Task Save_ReconcilesLanes_DeletesRemoved_UpsertsIncoming_AndStampsDefinitionId()
		{
			var command = new CommandDefinition
			{
				CommandDefinitionId = 5,
				DepartmentId = Dept,
				Name = "House Fire",
				Assignments = new List<CommandDefinitionRole>
				{
					new CommandDefinitionRole { CommandDefinitionRoleId = 50, Name = "Interior (renamed)" },
					new CommandDefinitionRole { CommandDefinitionRoleId = 0, Name = "Staging" }
				}
			};

			_definitions.Setup(x => x.SaveOrUpdateAsync(command, It.IsAny<CancellationToken>(), false))
				.ReturnsAsync(command);

			// Row 50 stays (updated), row 51 was removed by the caller and must be deleted.
			_roles.Setup(x => x.GetRolesByCommandDefinitionIdAsync(5)).ReturnsAsync(new List<CommandDefinitionRole>
			{
				new CommandDefinitionRole { CommandDefinitionRoleId = 50, CommandDefinitionId = 5, Name = "Interior" },
				new CommandDefinitionRole { CommandDefinitionRoleId = 51, CommandDefinitionId = 5, Name = "Old Lane" }
			});

			_roles.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandDefinitionRole>(), It.IsAny<CancellationToken>(), false))
				.ReturnsAsync((CommandDefinitionRole r, CancellationToken _, bool __) => r);

			var saved = await _service.Save(command, CancellationToken.None);

			_roles.Verify(x => x.DeleteAsync(It.Is<CommandDefinitionRole>(r => r.CommandDefinitionRoleId == 51), It.IsAny<CancellationToken>()), Times.Once);
			_roles.Verify(x => x.SaveOrUpdateAsync(It.Is<CommandDefinitionRole>(r => r.CommandDefinitionRoleId == 50 && r.CommandDefinitionId == 5), It.IsAny<CancellationToken>(), false), Times.Once);
			_roles.Verify(x => x.SaveOrUpdateAsync(It.Is<CommandDefinitionRole>(r => r.Name == "Staging" && r.CommandDefinitionId == 5), It.IsAny<CancellationToken>(), false), Times.Once);
			saved.Assignments.Should().HaveCount(2);
		}

		[Test]
		public async Task Save_ForeignRoleId_IsTreatedAsInsert_NotHijackedUpdate()
		{
			var command = new CommandDefinition
			{
				CommandDefinitionId = 5,
				DepartmentId = Dept,
				Assignments = new List<CommandDefinitionRole>
				{
					// Claims an id that does NOT belong to definition 5.
					new CommandDefinitionRole { CommandDefinitionRoleId = 999, Name = "Sneaky" }
				}
			};

			_definitions.Setup(x => x.SaveOrUpdateAsync(command, It.IsAny<CancellationToken>(), false))
				.ReturnsAsync(command);
			_roles.Setup(x => x.GetRolesByCommandDefinitionIdAsync(5)).ReturnsAsync(new List<CommandDefinitionRole>());
			_roles.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandDefinitionRole>(), It.IsAny<CancellationToken>(), false))
				.ReturnsAsync((CommandDefinitionRole r, CancellationToken _, bool __) => r);

			await _service.Save(command, CancellationToken.None);

			_roles.Verify(x => x.SaveOrUpdateAsync(It.Is<CommandDefinitionRole>(r => r.CommandDefinitionRoleId == 0 && r.Name == "Sneaky"), It.IsAny<CancellationToken>(), false), Times.Once);
		}

		[Test]
		public async Task Save_NullAssignments_LeavesExistingLanesUntouched()
		{
			var command = new CommandDefinition { CommandDefinitionId = 5, DepartmentId = Dept, Assignments = null };

			_definitions.Setup(x => x.SaveOrUpdateAsync(command, It.IsAny<CancellationToken>(), false))
				.ReturnsAsync(command);

			await _service.Save(command, CancellationToken.None);

			_roles.Verify(x => x.DeleteAsync(It.IsAny<CommandDefinitionRole>(), It.IsAny<CancellationToken>()), Times.Never);
			_roles.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CommandDefinitionRole>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Save_PersistsLaneRequirements_ReplacingExisting()
		{
			var command = new CommandDefinition
			{
				CommandDefinitionId = 5,
				DepartmentId = Dept,
				Assignments = new List<CommandDefinitionRole>
				{
					new CommandDefinitionRole
					{
						CommandDefinitionRoleId = 50,
						Name = "RIT",
						// duplicate unit type id collapses to one row
						RequiredUnitTypes = new List<CommandDefinitionRoleUnitType>
						{
							new CommandDefinitionRoleUnitType { UnitTypeId = 7 },
							new CommandDefinitionRoleUnitType { UnitTypeId = 7 }
						},
						RequiredRoles = new List<CommandDefinitionRolePersonnelRole>
						{
							new CommandDefinitionRolePersonnelRole { PersonnelRoleId = 3 }
						}
					}
				}
			};

			_definitions.Setup(x => x.SaveOrUpdateAsync(command, It.IsAny<CancellationToken>(), false))
				.ReturnsAsync(command);
			_roles.Setup(x => x.GetRolesByCommandDefinitionIdAsync(5)).ReturnsAsync(new List<CommandDefinitionRole>
			{
				new CommandDefinitionRole { CommandDefinitionRoleId = 50, CommandDefinitionId = 5, Name = "RIT" }
			});
			_roles.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandDefinitionRole>(), It.IsAny<CancellationToken>(), false))
				.ReturnsAsync((CommandDefinitionRole r, CancellationToken _, bool __) => r);

			// A stale requirement row exists and must be replaced.
			var staleUnitType = new CommandDefinitionRoleUnitType { CommandDefinitionRoleUnitTypeId = 1, CommandDefinitionRoleId = 50, UnitTypeId = 9 };
			_roleUnitTypes.Setup(x => x.GetUnitTypesByRoleIdAsync(50)).ReturnsAsync(new List<CommandDefinitionRoleUnitType> { staleUnitType });
			_roleUnitTypes.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandDefinitionRoleUnitType>(), It.IsAny<CancellationToken>(), false))
				.ReturnsAsync((CommandDefinitionRoleUnitType r, CancellationToken _, bool __) => r);
			_rolePersonnelRoles.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandDefinitionRolePersonnelRole>(), It.IsAny<CancellationToken>(), false))
				.ReturnsAsync((CommandDefinitionRolePersonnelRole r, CancellationToken _, bool __) => r);

			await _service.Save(command, CancellationToken.None);

			_roleUnitTypes.Verify(x => x.DeleteAsync(staleUnitType, It.IsAny<CancellationToken>()), Times.Once);
			_roleUnitTypes.Verify(x => x.SaveOrUpdateAsync(It.Is<CommandDefinitionRoleUnitType>(r => r.UnitTypeId == 7 && r.CommandDefinitionRoleId == 50), It.IsAny<CancellationToken>(), false), Times.Once);
			_rolePersonnelRoles.Verify(x => x.SaveOrUpdateAsync(It.Is<CommandDefinitionRolePersonnelRole>(r => r.PersonnelRoleId == 3 && r.CommandDefinitionRoleId == 50), It.IsAny<CancellationToken>(), false), Times.Once);
		}

		[Test]
		public async Task GetRoleWithRequirementsAsync_HydratesRequirementSets()
		{
			_roles.Setup(x => x.GetByIdAsync(50)).ReturnsAsync(new CommandDefinitionRole { CommandDefinitionRoleId = 50, ForceRequirements = true });
			_roleUnitTypes.Setup(x => x.GetUnitTypesByRoleIdAsync(50)).ReturnsAsync(new List<CommandDefinitionRoleUnitType>
			{
				new CommandDefinitionRoleUnitType { CommandDefinitionRoleId = 50, UnitTypeId = 7 }
			});
			_rolePersonnelRoles.Setup(x => x.GetPersonnelRolesByRoleIdAsync(50)).ReturnsAsync(new List<CommandDefinitionRolePersonnelRole>
			{
				new CommandDefinitionRolePersonnelRole { CommandDefinitionRoleId = 50, PersonnelRoleId = 3 }
			});

			var role = await _service.GetRoleWithRequirementsAsync(50);

			role.Should().NotBeNull();
			role.RequiredUnitTypes.Select(x => x.UnitTypeId).Should().BeEquivalentTo(new[] { 7 });
			role.RequiredRoles.Select(x => x.PersonnelRoleId).Should().BeEquivalentTo(new[] { 3 });
		}

		[Test]
		public async Task DeleteAsync_DeletesChildLanes_BeforeTheDefinition()
		{
			var command = new CommandDefinition { CommandDefinitionId = 5, DepartmentId = Dept };

			_roles.Setup(x => x.GetRolesByCommandDefinitionIdAsync(5)).ReturnsAsync(new List<CommandDefinitionRole>
			{
				new CommandDefinitionRole { CommandDefinitionRoleId = 50, CommandDefinitionId = 5 }
			});
			_definitions.Setup(x => x.DeleteAsync(command, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var result = await _service.DeleteAsync(command, CancellationToken.None);

			result.Should().BeTrue();
			_roles.Verify(x => x.DeleteAsync(It.Is<CommandDefinitionRole>(r => r.CommandDefinitionRoleId == 50), It.IsAny<CancellationToken>()), Times.Once);
			_definitions.Verify(x => x.DeleteAsync(command, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
