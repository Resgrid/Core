using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// The coverage pass: related rows that enrich the existing families (caller, custom fields, station and role names,
	/// member identification numbers, contact addresses and notes) and the operations reference families (protocols,
	/// trainings, calendar events, logs, POIs, shifts, groups, occupancies), each under the protected-text rule.
	/// </summary>
	[TestFixture]
	public class SearchCoverageProjectionTests
	{
		private const int Dept = 7;
		private bool _enforced;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<ISearchProjectionsRepository> _projections;
		private Mock<IAddressRepository> _addresses;
		private Mock<IContactNotesRepository> _contactNotes;
		private Mock<IContactCategoryRepository> _categories;
		private Mock<IPersonnelRolesRepository> _roles;
		private Mock<IDepartmentGroupMembersRepository> _groupMembers;
		private Mock<IDepartmentGroupsRepository> _groups;
		private Mock<IDepartmentMemberSensitiveDataRepository> _sensitive;
		private Mock<IUdfDefinitionRepository> _udfDefinitions;
		private Mock<IUdfFieldRepository> _udfFields;
		private Mock<IUdfFieldValueRepository> _udfValues;
		private Mock<ICallsRepository> _calls;
		private Mock<IUserProfilesRepository> _profiles;
		private Mock<IDepartmentMembersRepository> _members;
		private Mock<IRmsOccupancyHazardsRepository> _hazards;
		private List<SearchProjection> _upserts;
		private List<(string Type, string Id)> _removed;
		private SearchProjectionService _service;

		[SetUp]
		public void SetUp()
		{
			_enforced = false;
			_upserts = new List<SearchProjection>();
			_removed = new List<(string, string)>();
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(p => p.IsProtectionEnforcedAsync(It.IsAny<int>())).ReturnsAsync(() => _enforced);
			_projections = new Mock<ISearchProjectionsRepository>();
			_projections.Setup(r => r.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>()))
				.Callback((SearchProjection p, CancellationToken _) => _upserts.Add(p)).ReturnsAsync((SearchProjection p, CancellationToken _) => p);
			_projections.Setup(r => r.SoftDeleteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((int _, string type, string id, CancellationToken __) => _removed.Add((type, id))).ReturnsAsync(true);

			_addresses = new Mock<IAddressRepository>();
			_addresses.Setup(a => a.GetByIdAsync(11)).ReturnsAsync(new Address { AddressId = 11, Address1 = "400 Industrial Way", City = "Springfield", State = "IL", PostalCode = "62701" });
			_contactNotes = new Mock<IContactNotesRepository>();
			_contactNotes.Setup(n => n.GetContactNotesByContactIdAsync("c1")).ReturnsAsync(new List<ContactNote>
			{
				new ContactNote { ContactId = "c1", DepartmentId = Dept, Note = "Alarm panel in the east stairwell", AddedOn = new DateTime(2026, 1, 1) },
				new ContactNote { ContactId = "c1", DepartmentId = Dept, Note = "Old key holder list", IsDeleted = true, AddedOn = new DateTime(2025, 1, 1) }
			});
			_categories = new Mock<IContactCategoryRepository>();
			_categories.Setup(c => c.GetByIdAsync("cat1")).ReturnsAsync(new ContactCategory { ContactCategoryId = "cat1", DepartmentId = Dept, Name = "Alarm Companies" });
			_roles = new Mock<IPersonnelRolesRepository>();
			_roles.Setup(r => r.GetRolesForUserAsync(Dept, "u1")).ReturnsAsync(new List<PersonnelRole>
			{
				new PersonnelRole { DepartmentId = Dept, Name = "Engineer" }, new PersonnelRole { DepartmentId = Dept, Name = "Captain" }, new PersonnelRole { DepartmentId = 99, Name = "Elsewhere" }
			});
			_groupMembers = new Mock<IDepartmentGroupMembersRepository>();
			_groups = new Mock<IDepartmentGroupsRepository>();
			_groups.Setup(g => g.GetGroupByGroupIdAsync(4)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = Dept, Name = "Station 4" });
			_sensitive = new Mock<IDepartmentMemberSensitiveDataRepository>();
			_sensitive.Setup(s => s.GetByDepartmentAndUserAsync(Dept, "u1")).ReturnsAsync(new DepartmentMemberSensitiveData { DepartmentId = Dept, UserId = "u1", IdentificationNumber = "FF-1042" });
			_udfDefinitions = new Mock<IUdfDefinitionRepository>();
			_udfDefinitions.Setup(d => d.GetActiveDefinitionByDepartmentAndEntityTypeAsync(Dept, (int)UdfEntityType.Call)).ReturnsAsync(new UdfDefinition { UdfDefinitionId = "def-call" });
			_udfFields = new Mock<IUdfFieldRepository>();
			_udfFields.Setup(f => f.GetFieldsByDefinitionIdAsync("def-call")).ReturnsAsync(new List<UdfField>
			{
				new UdfField { UdfFieldId = "f1", Label = "Alarm Account", IsEnabled = true, Visibility = (int)UdfFieldVisibility.Everyone, Sensitivity = (int)UdfFieldSensitivity.None, SortOrder = 1 },
				new UdfField { UdfFieldId = "f2", Label = "Admin Memo", IsEnabled = true, Visibility = (int)UdfFieldVisibility.DepartmentAdminsOnly, SortOrder = 2 },
				new UdfField { UdfFieldId = "f3", Label = "Restricted", IsEnabled = true, Visibility = (int)UdfFieldVisibility.Everyone, Sensitivity = (int)UdfFieldSensitivity.Restricted, SortOrder = 3 }
			});
			_udfValues = new Mock<IUdfFieldValueRepository>();
			_udfValues.Setup(v => v.GetFieldValuesByEntityAsync((int)UdfEntityType.Call, "42", "def-call")).ReturnsAsync(new List<UdfFieldValue>
			{
				new UdfFieldValue { UdfFieldId = "f1", Value = "ACME-5521" }, new UdfFieldValue { UdfFieldId = "f2", Value = "do not index this" }, new UdfFieldValue { UdfFieldId = "f3", Value = "nor this" }
			});
			_calls = new Mock<ICallsRepository>();
			_profiles = new Mock<IUserProfilesRepository>();
			_members = new Mock<IDepartmentMembersRepository>();
			_hazards = new Mock<IRmsOccupancyHazardsRepository>();

			_service = new SearchProjectionService(_projections.Object, _protection.Object, null, null, _addresses.Object, _contactNotes.Object,
				_categories.Object, _roles.Object, _groupMembers.Object, _groups.Object, _sensitive.Object, _udfDefinitions.Object, _udfFields.Object,
				_udfValues.Object, _calls.Object, null, null, _profiles.Object, _members.Object, null, null, _hazards.Object);
		}

		private static Call Call() => new Call
		{
			CallId = 42, DepartmentId = Dept, Number = "2026-000042", Name = "Commercial Alarm", NatureOfCall = "Trouble alarm", Type = "Alarm",
			ContactName = "ACME Monitoring", ContactNumber = "(555) 010-2233", LoggedOn = new DateTime(2026, 5, 1)
		};

		[Test]
		public async Task A_call_carries_its_caller_and_the_custom_fields_every_viewer_may_see()
		{
			var p = await _service.BuildCallAsync(Call());

			p.SearchText.Should().Contain("ACME Monitoring").And.Contain("(555) 010-2233").And.Contain("Alarm Account ACME-5521");
			p.SearchText.Should().NotContain("do not index this", "an admin-only field never enters the shared index");
			p.SearchText.Should().NotContain("nor this", "a restricted field never enters the shared index");
			p.Keywords.Should().Contain("5550102233", "the caller's number is searchable by its digits");
		}

		[Test]
		public async Task Enforced_protection_keeps_the_caller_and_custom_fields_out()
		{
			_enforced = true;

			var p = await _service.BuildCallAsync(Call());

			(p.SearchText ?? string.Empty).Should().NotContain("ACME");
			p.Keywords.Should().Be("2026-000042");
			_udfValues.Verify(v => v.GetFieldValuesByEntityAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task A_member_carries_the_department_identification_number_group_and_roles()
		{
			var profile = new UserProfile { UserId = "u1", FirstName = "Kelly", LastName = "Severide", IdentificationNumber = "legacy-global" };

			var p = await _service.BuildPersonnelAsync(Dept, profile, 4, true);

			p.Keywords.Should().Contain("FF-1042").And.NotContain("legacy-global", "the per-department row wins over the unmapped legacy column");
			p.Summary.Should().Be("Station 4 · Captain, Engineer");
			p.SearchText.Should().Contain("Station 4").And.Contain("Engineer").And.NotContain("Elsewhere");
		}

		[Test]
		public async Task A_unit_carries_its_station_name()
		{
			var p = await _service.BuildUnitAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 4", Type = "Engine", StationGroupId = 4 });

			p.Summary.Should().Be("Engine · Station 4");
			p.SearchText.Should().Contain("Station 4");
		}

		[Test]
		public async Task A_contact_carries_its_address_live_notes_and_category()
		{
			var p = await _service.BuildContactAsync(new Contact
			{
				ContactId = "c1", DepartmentId = Dept, ContactType = 1, CompanyName = "ACME Monitoring", PhysicalAddressId = 11, ContactCategoryId = "cat1"
			});

			p.SearchText.Should().Contain("400 Industrial Way Springfield IL 62701").And.Contain("Alarm panel in the east stairwell").And.Contain("Alarm Companies");
			p.SearchText.Should().NotContain("Old key holder list", "deleted notes leave the projection");
			p.Summary.Should().Contain("400 Industrial Way");
		}

		[Test]
		public async Task Refreshing_a_member_who_was_disabled_removes_the_projection()
		{
			_members.Setup(m => m.GetDepartmentMemberByDepartmentIdAndUserIdAsync(Dept, "u1")).ReturnsAsync(new DepartmentMember { DepartmentId = Dept, UserId = "u1", IsDisabled = true });

			await _service.RefreshAsync(Dept, SearchEntityTypes.Personnel, "u1");

			_removed.Should().Contain((SearchEntityTypes.Personnel, "u1"));
			_upserts.Should().BeEmpty();
		}

		[Test]
		public async Task Refreshing_a_member_reads_the_current_group_and_rewrites_the_projection()
		{
			_members.Setup(m => m.GetDepartmentMemberByDepartmentIdAndUserIdAsync(Dept, "u1")).ReturnsAsync(new DepartmentMember { DepartmentId = Dept, UserId = "u1", IsActive = true });
			_profiles.Setup(p => p.GetProfileByUserIdAsync("u1")).ReturnsAsync(new UserProfile { UserId = "u1", FirstName = "Kelly", LastName = "Severide" });
			_groupMembers.Setup(g => g.GetAllGroupMembersByUserAndDepartmentAsync("u1", Dept)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentId = Dept, DepartmentGroupId = 4, UserId = "u1" } });

			await _service.RefreshAsync(Dept, SearchEntityTypes.Personnel, "u1");

			_upserts.Should().ContainSingle(p => p.EntityType == SearchEntityTypes.Personnel && p.GroupId == 4 && p.Summary.StartsWith("Station 4"));
		}

		[Test]
		public async Task Refreshing_a_call_from_another_department_does_nothing()
		{
			_calls.Setup(c => c.GetByIdAsync(42)).ReturnsAsync(new Call { CallId = 42, DepartmentId = 99, Name = "Elsewhere" });

			await _service.RefreshAsync(Dept, SearchEntityTypes.Call, "42");

			_upserts.Should().BeEmpty();
		}

		[Test]
		public async Task A_log_carries_its_narrative_only_where_protection_allows_it()
		{
			var log = new Log
			{
				LogId = 9, DepartmentId = Dept, LogType = (int)LogTypes.Training, Course = "Ladder Operations", CourseCode = "LAD-2",
				Instructors = "Capt. Casey", Narrative = "<p>Raised the 35 ft ladder at Bldg 4</p>", LoggedOn = new DateTime(2026, 2, 2), StationGroupId = 4
			};

			var open = await _service.BuildLogAsync(log);
			_enforced = true;
			var closed = await _service.BuildLogAsync(log);

			open.Title.Should().Be("Training · Ladder Operations");
			open.SearchText.Should().Contain("Raised the 35 ft ladder at Bldg 4").And.Contain("Capt. Casey");
			open.Keywords.Should().Contain("LAD-2");
			open.Url.Should().Be("/User/Logs/View?logId=9");
			closed.SearchText.Should().Contain("Ladder Operations").And.NotContain("Raised the 35 ft ladder", "the narrative is cataloged");
		}

		[Test]
		public async Task A_calendar_event_is_indexed_once_per_series_and_never_under_enforced_protection()
		{
			var parent = new CalendarItem { CalendarItemId = 3, DepartmentId = Dept, Title = "Monthly business meeting", Location = "Station 4", Start = new DateTime(2026, 6, 1), End = new DateTime(2026, 6, 1, 2, 0, 0), RecurrenceType = 2 };
			var occurrence = new CalendarItem { CalendarItemId = 4, DepartmentId = Dept, Title = "Monthly business meeting", RecurrenceId = "3", Start = new DateTime(2026, 7, 1), End = new DateTime(2026, 7, 1, 2, 0, 0) };

			(await _service.BuildCalendarItemAsync(parent)).Title.Should().Be("Monthly business meeting");
			(await _service.BuildCalendarItemAsync(occurrence)).Should().BeNull("occurrences repeat the parent's text");
			_enforced = true;
			(await _service.BuildCalendarItemAsync(parent)).Should().BeNull("calendar titles, descriptions and locations are cataloged");
		}

		[Test]
		public async Task Reference_families_project_names_codes_and_their_own_pages()
		{
			var protocol = await _service.BuildProtocolAsync(new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = Dept, Name = "Commercial Fire Alarm", Code = "CFA", ProtocolText = "<b>Stage</b> until keyholder arrives" });
			var training = await _service.BuildTrainingAsync(new Training { TrainingId = 2, DepartmentId = Dept, Name = "Hazmat Awareness", TrainingText = "Placard identification" });
			var shift = await _service.BuildShiftAsync(new Shift { ShiftId = 3, DepartmentId = Dept, Name = "B Shift", Code = "B", StartTime = "07:00", EndTime = "07:00" });
			var group = await _service.BuildGroupAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = Dept, Name = "Station 4", Type = (int)DepartmentGroupTypes.Station, AddressId = 11 });
			var poi = await _service.BuildPoiAsync(new Poi { PoiId = 5, PoiTypeId = 6, Name = "Dry hydrant", Address = "Route 9", Note = "Pond access" }, new PoiType { PoiTypeId = 6, DepartmentId = Dept, Name = "Water Sources" });

			protocol.Keywords.Should().Be("CFA");
			protocol.SearchText.Should().Contain("Stage until keyholder arrives");
			protocol.Url.Should().Be("/User/Protocols/View?id=1");
			training.SearchText.Should().Contain("Placard identification");
			training.Url.Should().Be("/User/Trainings/View?trainingId=2");
			shift.Summary.Should().Be("B · 07:00–07:00");
			group.Category.Should().Be("Station");
			group.SearchText.Should().Contain("400 Industrial Way");
			poi.DepartmentId.Should().Be(Dept, "a POI takes its department from its type");
			poi.SearchText.Should().Contain("Water Sources").And.Contain("Pond access");
			poi.Url.Should().Be("/User/Mapping/EditPOI?poiId=5");
		}

		[Test]
		public async Task A_poi_whose_type_does_not_match_is_not_projected()
		{
			(await _service.BuildPoiAsync(new Poi { PoiId = 5, PoiTypeId = 6, Name = "Dry hydrant" }, new PoiType { PoiTypeId = 7, DepartmentId = Dept, Name = "Other" })).Should().BeNull();
		}

		[Test]
		public async Task An_occupancy_carries_its_address_alarm_company_and_hazards_but_never_access_codes()
		{
			_hazards.Setup(h => h.GetForOccupancyAsync(Dept, "o1")).ReturnsAsync(new List<RmsOccupancyHazard>
			{
				new RmsOccupancyHazard { Title = "Ammonia refrigeration" }, new RmsOccupancyHazard { Title = "Removed hazard", DeletedOn = DateTime.UtcNow }
			});
			var occupancy = new RmsOccupancy
			{
				RmsOccupancyId = "o1", DepartmentId = Dept, Name = "Springfield Cold Storage", OccupancyNumber = "OCC-2026-0007", AddressText = "400 Industrial Way",
				City = "Springfield", Status = (int)RmsOccupancyStatus.Active, AlarmCompany = "ACME Monitoring", AlarmCompanyPhone = "555-010-2233",
				GateCode = "4471", KnoxBoxLocation = "Left of the main door", EmergencyContactPhone = "555-999-0000", ModifiedOn = new DateTime(2026, 3, 3)
			};

			var p = await _service.BuildOccupancyAsync(occupancy);

			p.Keywords.Should().Contain("OCC-2026-0007").And.Contain("5550102233");
			p.SearchText.Should().Contain("400 Industrial Way").And.Contain("ACME Monitoring").And.Contain("Ammonia refrigeration").And.NotContain("Removed hazard");
			string.Join(" ", p.Title, p.Summary, p.Keywords, p.SearchText).Should().NotContain("4471").And.NotContain("Left of the main door").And.NotContain("5559990000");
			p.Url.Should().Be("/User/RecordOccupancies/Details?id=o1");

			occupancy.Status = (int)RmsOccupancyStatus.Merged;
			(await _service.BuildOccupancyAsync(occupancy)).Should().BeNull("a merged occupancy is found through its survivor");
		}
	}
}
