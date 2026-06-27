using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Covers <see cref="SyncService.GetReferenceDataAsync"/>: the shift-start reference aggregate. Focuses on the
	/// SAFE personnel projection (group + current-state join, and that the DTO structurally cannot carry the
	/// UserProfile secrets) plus passthrough of the raw config lists.
	/// </summary>
	[TestFixture]
	public class SyncServiceTests
	{
		private const int Dept = 10;

		private Mock<ICallsService> _calls;
		private Mock<ICommandsService> _commands;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IMappingService> _mapping;
		private Mock<IProtocolsService> _protocols;
		private Mock<ICheckInTimerService> _checkInTimers;
		private Mock<ICustomStateService> _customStates;
		private Mock<IUserProfileService> _profiles;
		private Mock<IUserStateService> _userStates;
		private Mock<IFeatureToggleService> _features;
		private SyncService _service;

		[SetUp]
		public void SetUp()
		{
			_calls = new Mock<ICallsService>();
			_commands = new Mock<ICommandsService>();
			_units = new Mock<IUnitsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_mapping = new Mock<IMappingService>();
			_protocols = new Mock<IProtocolsService>();
			_checkInTimers = new Mock<ICheckInTimerService>();
			_customStates = new Mock<ICustomStateService>();
			_profiles = new Mock<IUserProfileService>();
			_userStates = new Mock<IUserStateService>();
			_features = new Mock<IFeatureToggleService>();

			// Unset methods return null under Loose mocks; SyncService coalesces those to empty lists.
			_service = new SyncService(_calls.Object, _commands.Object, _units.Object, _groups.Object,
				_mapping.Object, _protocols.Object, _checkInTimers.Object, _customStates.Object,
				_profiles.Object, _userStates.Object, _features.Object);
		}

		[Test]
		public async Task GetReferenceDataAsync_ProjectsSafePersonnel_WithGroupAndState()
		{
			_calls.Setup(x => x.GetCallTypesForDepartmentAsync(Dept))
				.ReturnsAsync(new List<CallType> { new CallType { CallTypeId = 1, DepartmentId = Dept } });

			_profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(new Dictionary<string, UserProfile>
				{
					["u1"] = new UserProfile
					{
						UserId = "u1", FirstName = "John", LastName = "Doe", MobileNumber = "5551234",
						// Secrets that must NEVER reach the client — ReferencePersonnel structurally cannot carry them.
						EmailVerificationCode = "SECRET", CalendarSyncToken = "SECRET-TOKEN"
					}
				});

			_groups.Setup(x => x.GetAllGroupsForDepartmentAsync(Dept))
				.ReturnsAsync(new List<DepartmentGroup>
				{
					new DepartmentGroup
					{
						DepartmentGroupId = 5, DepartmentId = Dept, Name = "Station 1",
						Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "u1", DepartmentGroupId = 5 } }
					}
				});

			_userStates.Setup(x => x.GetStatesForDepartmentAsync(Dept))
				.ReturnsAsync(new List<UserState> { new UserState { UserId = "u1", State = 2, Timestamp = DateTime.UtcNow } });

			var data = await _service.GetReferenceDataAsync(Dept);

			data.ServerTimestampMs.Should().BeGreaterThan(0);
			data.CallTypes.Should().ContainSingle();                          // raw config list passthrough
			data.Groups.Should().ContainSingle().Which.GroupId.Should().Be(5); // projected to the safe ReferenceGroup

			var person = data.Personnel.Should().ContainSingle().Subject;
			person.UserId.Should().Be("u1");
			person.FirstName.Should().Be("John");
			person.LastName.Should().Be("Doe");
			person.MobilePhone.Should().Be("5551234");
			person.GroupId.Should().Be(5);
			person.GroupName.Should().Be("Station 1");
			person.StateId.Should().Be(2);
			person.StateTimestamp.Should().NotBeNull();
		}

		[Test]
		public async Task GetReferenceDataAsync_ReturnsEmptyPersonnel_WhenNoProfiles()
		{
			_profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(new Dictionary<string, UserProfile>());

			var data = await _service.GetReferenceDataAsync(Dept);

			data.Personnel.Should().BeEmpty();
			data.ServerTimestampMs.Should().BeGreaterThan(0);
		}
	}
}
