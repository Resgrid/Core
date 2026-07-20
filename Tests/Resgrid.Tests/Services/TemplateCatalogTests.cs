using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.CommandBoards;
using Resgrid.Model.UnitRoles;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class TemplateCatalogTests
	{
		private static readonly string[] ExpectedOperationalCategories =
		{
			"Fire Departments",
			"EMS",
			"Law Enforcement",
			"Search and Rescue",
			"Emergency Management",
			"Disaster Response",
			"Security Companies",
			"Event Medical / Security",
			"Industrial Response",
			"Delivery Companies"
		};

		[Test]
		public void UnitRoleCatalog_AllTemplates_HasBroadUniqueCodeDefinedCoverage()
		{
			// Arrange
			var templates = UnitRoleTemplateCatalog.All;

			// Act
			var categories = templates.Select(template => template.Category).Distinct().ToList();
			var ids = templates.Select(template => template.Id).ToList();

			// Assert
			templates.Should().HaveCountGreaterThan(30);
			categories.Should().Contain(ExpectedOperationalCategories);
			ids.Should().OnlyHaveUniqueItems();
			templates.Should().OnlyContain(template =>
				!string.IsNullOrWhiteSpace(template.Id) &&
				!string.IsNullOrWhiteSpace(template.Name) &&
				template.Roles.Count > 0 &&
				template.Roles.All(role => !string.IsNullOrWhiteSpace(role.Name)));
		}

		[Test]
		public void UnitRoleCatalog_Search_MatchesCategoryKeywordsAndSeatNames()
		{
			// Arrange
			const string query = "event medic";

			// Act
			var results = UnitRoleTemplateCatalog.Search(query);

			// Assert
			results.Should().Contain(template => template.Id == "event-medical-roving");
			results.Should().OnlyContain(template =>
				template.SearchText.Contains("event") && template.SearchText.Contains("medic"));
		}

		[Test]
		public void CommandBoardCatalog_AllTemplates_HasBroadUniqueCodeDefinedCoverage()
		{
			// Arrange
			var templates = CommandBoardTemplateCatalog.All;

			// Act
			var categories = templates.Select(template => template.Category).Distinct().ToList();
			var ids = templates.Select(template => template.Id).ToList();

			// Assert
			templates.Should().HaveCountGreaterThan(20);
			categories.Should().Contain(ExpectedOperationalCategories);
			ids.Should().OnlyHaveUniqueItems();
			templates.Should().OnlyContain(template =>
				!string.IsNullOrWhiteSpace(template.Id) &&
				!string.IsNullOrWhiteSpace(template.Name) &&
				template.Lanes.Count > 0 &&
				template.Lanes.All(lane => !string.IsNullOrWhiteSpace(lane.Name)));
		}

		[Test]
		public void CommandBoardCatalog_Search_MatchesAcrossKeywordsAndLaneNames()
		{
			// Arrange
			const string query = "ambulance staging";

			// Act
			var results = CommandBoardTemplateCatalog.Search(query);

			// Assert
			results.Should().Contain(template => template.Id == "ems-mass-casualty");
			results.Should().OnlyContain(template =>
				template.SearchText.Contains("ambulance") && template.SearchText.Contains("staging"));
		}

		[Test]
		public void CreateDefinition_MatchingDepartmentReferenceData_SeedsEditableRequirements()
		{
			// Arrange
			var template = new CommandBoardTemplate
			{
				Name = "Example Board",
				Description = "Example description",
				Timer = true,
				TimerMinutes = 15,
				Lanes = new List<CommandBoardTemplateLane>
				{
					new CommandBoardTemplateLane
					{
						Name = "Fire Attack",
						Description = "Interior operations",
						LaneType = CommandNodeType.Group,
						SuggestedUnitTypes = new[] { " engine " },
						SuggestedPersonnelRoles = new[] { "firefighter" },
						ForceRequirements = true
					}
				}
			};
			var unitTypes = new[] { new UnitType { UnitTypeId = 10, Type = "Engine" } };
			var personnelRoles = new[] { new PersonnelRole { PersonnelRoleId = 20, Name = "Firefighter" } };

			// Act
			var definition = template.CreateDefinition(unitTypes, personnelRoles);
			var assignment = definition.Assignments.Single();

			// Assert
			definition.CommandDefinitionId.Should().Be(0);
			definition.DepartmentId.Should().Be(0);
			definition.Name.Should().Be("Example Board");
			definition.Timer.Should().BeTrue();
			definition.TimerMinutes.Should().Be(15);
			assignment.Name.Should().Be("Fire Attack");
			assignment.LaneType.Should().Be((int)CommandNodeType.Group);
			assignment.SortOrder.Should().Be(0);
			assignment.RequiredUnitTypes.Should().ContainSingle(item => item.UnitTypeId == 10);
			assignment.RequiredRoles.Should().ContainSingle(item => item.PersonnelRoleId == 20);
			assignment.ForceRequirements.Should().BeTrue();
		}

		[Test]
		public void CreateDefinition_UnmatchedSuggestions_LeavesLaneUsableAndAdvisory()
		{
			// Arrange
			var template = new CommandBoardTemplate
			{
				Name = "Example Board",
				Lanes = new List<CommandBoardTemplateLane>
				{
					new CommandBoardTemplateLane
					{
						Name = "Entry Team",
						SuggestedPersonnelRoles = new[] { "Hazmat Technician" },
						ForceRequirements = true
					}
				}
			};

			// Act
			var definition = template.CreateDefinition(null, null);
			var assignment = definition.Assignments.Single();

			// Assert
			assignment.RequiredUnitTypes.Should().BeEmpty();
			assignment.RequiredRoles.Should().BeEmpty();
			assignment.ForceRequirements.Should().BeFalse();
		}
	}
}
