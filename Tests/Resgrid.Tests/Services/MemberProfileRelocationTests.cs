using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository.Extensions;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Moving a member's identification number and addresses off the global UserProfiles row onto
	/// their department-scoped row (ADP plan 5.1). M0134 does this in SQL for departments that are
	/// still plaintext; this service is the path for the rest — already-enrolled departments, where
	/// the value has to be enveloped as it lands, and members who join after the migration ran.
	///
	/// The invariants that matter: never overwrite a value the department already has, never push a
	/// legacy value back onto a member who deliberately cleared theirs, and always leave a marker so
	/// the backlog actually drains.
	/// </summary>
	[TestFixture]
	public class MemberProfileRelocationTests
	{
		private const int DeptId = 42;

		private Mock<IDepartmentMemberSensitiveDataRepository> _repo;
		private Mock<IDepartmentMemberSensitiveDataService> _sensitiveDataService;
		private Mock<IUserProfileService> _userProfileService;
		private Mock<IAddressService> _addressService;
		private List<DepartmentMemberSensitiveData> _saved;
		private MemberProfileRelocationService _service;

		[SetUp]
		public void SetUp()
		{
			_repo = new Mock<IDepartmentMemberSensitiveDataRepository>();
			_sensitiveDataService = new Mock<IDepartmentMemberSensitiveDataService>();
			_userProfileService = new Mock<IUserProfileService>();
			_addressService = new Mock<IAddressService>();
			_saved = new List<DepartmentMemberSensitiveData>();

			_sensitiveDataService.Setup(x => x.SaveAsync(It.IsAny<DepartmentMemberSensitiveData>(),
					It.IsAny<CancellationToken>()))
				.Returns<DepartmentMemberSensitiveData, CancellationToken>((d, ct) =>
				{
					_saved.Add(d);
					return Task.FromResult(d);
				});

			_repo.Setup(x => x.GetAllByDepartmentIdAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<DepartmentMemberSensitiveData>());

			_service = new MemberProfileRelocationService(_repo.Object, _sensitiveDataService.Object,
				_userProfileService.Object, _addressService.Object);
		}

		private void SetupProfiles(params UserProfile[] profiles) =>
			_userProfileService.Setup(x => x.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DeptId))
				.ReturnsAsync(profiles.ToDictionary(p => p.UserId));

		private void SetupRows(params DepartmentMemberSensitiveData[] rows) =>
			_repo.Setup(x => x.GetAllByDepartmentIdAsync(DeptId)).ReturnsAsync(rows.ToList());

		private void SetupAddress(int addressId, string line1) =>
			_addressService.Setup(x => x.GetAddressByIdAsync(addressId))
				.ReturnsAsync(new Address
				{
					AddressId = addressId,
					Address1 = line1,
					City = "Springfield",
					State = "IL",
					PostalCode = "62701",
					Country = "US"
				});

		[Test]
		public async Task Legacy_identification_number_and_addresses_move_onto_the_department_row()
		{
			SetupProfiles(new UserProfile
			{
				UserId = "user-1",
				IdentificationNumber = "BADGE-7",
				HomeAddressId = 100,
				MailingAddressId = 200
			});
			SetupAddress(100, "1 Home Street");
			SetupAddress(200, "2 Mailing Street");

			var result = await _service.RelocateDepartmentAsync(DeptId);

			result.RowsCreated.Should().Be(1);
			result.IdentificationNumbersMoved.Should().Be(1);
			result.AddressesMoved.Should().Be(2);
			result.Failures.Should().Be(0);

			var row = _saved.Single();
			row.DepartmentId.Should().Be(DeptId);
			row.IdentificationNumber.Should().Be("BADGE-7");
			row.HomeAddress1.Should().Be("1 Home Street");
			row.HomeCity.Should().Be("Springfield");
			row.HomePostalCode.Should().Be("62701");
			row.MailingAddress1.Should().Be("2 Mailing Street");
			row.LegacyProfileRelocatedOn.Should().NotBeNull();
		}

		[Test]
		public void Legacy_profile_fields_are_readable_for_relocation_but_not_generically_written()
		{
			// Dapper maps SELECT * independently of EF's NotMapped attribute and the custom write
			// exclusions, so it still hydrates the legacy sources while the generic Resgrid
			// insert/update builder keeps current application writes away from the old columns.
			var table = new DataTable();
			table.Columns.Add(nameof(UserProfile.UserId), typeof(string));
			table.Columns.Add(nameof(UserProfile.IdentificationNumber), typeof(string));
			table.Columns.Add(nameof(UserProfile.HomeAddressId), typeof(int));
			table.Columns.Add(nameof(UserProfile.MailingAddressId), typeof(int));
			table.Rows.Add("user-1", "BADGE-7", 101, 202);

			using var reader = table.CreateDataReader();
			reader.Read().Should().BeTrue();
			var profile = reader.GetRowParser<UserProfile>()(reader);

			profile.IdentificationNumber.Should().Be("BADGE-7",
				"MemberProfileRelocationService must still be able to read the retained source column");
			profile.HomeAddressId.Should().Be(101);
			profile.MailingAddressId.Should().Be(202);
			profile.IgnoredProperties.Should().Contain(new[]
			{
				nameof(UserProfile.IdentificationNumber), nameof(UserProfile.HomeAddressId),
				nameof(UserProfile.MailingAddressId)
			}, "new profile inserts and edits must never write the legacy global values");
			var writeColumns = profile.GetColumns(new SqlServerConfiguration(),
				ignoreProperties: profile.IgnoredProperties).ToList();
			writeColumns.Should().NotContain(column =>
				column.Contains(nameof(UserProfile.IdentificationNumber), StringComparison.OrdinalIgnoreCase) ||
				column.Contains(nameof(UserProfile.HomeAddressId), StringComparison.OrdinalIgnoreCase) ||
				column.Contains(nameof(UserProfile.MailingAddressId), StringComparison.OrdinalIgnoreCase));
			JsonConvert.SerializeObject(profile).Should().NotContain($"\"{nameof(UserProfile.IdentificationNumber)}\":",
				"the temporary plaintext source must not bypass the department-scoped read path");
		}

		[Test]
		public async Task A_department_specific_value_is_never_overwritten()
		{
			// The whole point of the move is that these differ per department. Anything already in
			// the target wins over whatever the shared profile happens to hold.
			SetupProfiles(new UserProfile
			{
				UserId = "user-1",
				IdentificationNumber = "GLOBAL-1",
				HomeAddressId = 100,
				MailingAddressId = 200
			});
			SetupRows(new DepartmentMemberSensitiveData
			{
				DepartmentMemberSensitiveDataId = 5,
				DepartmentId = DeptId,
				UserId = "user-1",
				IdentificationNumber = "DEPT-9",
				HomeAddress1 = "9 Department Road"
			});
			SetupAddress(100, "1 Home Street");
			SetupAddress(200, "2 Mailing Street");

			var result = await _service.RelocateDepartmentAsync(DeptId);

			var row = _saved.Single();
			row.IdentificationNumber.Should().Be("DEPT-9");
			row.HomeAddress1.Should().Be("9 Department Road");

			// The empty one still gets filled — this is a per-field move, not all-or-nothing.
			row.MailingAddress1.Should().Be("2 Mailing Street");
			result.IdentificationNumbersMoved.Should().Be(0);
			result.AddressesMoved.Should().Be(1);
			result.RowsCreated.Should().Be(0);
		}

		[Test]
		public async Task An_already_marked_member_is_left_alone()
		{
			SetupProfiles(new UserProfile { UserId = "user-1", IdentificationNumber = "BADGE-7" });
			SetupRows(new DepartmentMemberSensitiveData
			{
				DepartmentMemberSensitiveDataId = 5,
				DepartmentId = DeptId,
				UserId = "user-1",
				LegacyProfileRelocatedOn = DateTime.UtcNow.AddDays(-3)
			});

			var result = await _service.RelocateDepartmentAsync(DeptId);

			result.MembersExamined.Should().Be(0);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task A_cleared_department_value_is_not_refilled_from_the_legacy_profile()
		{
			// A member who deliberately clears their department identification number must not have
			// the old global one pushed back onto them by the next pass. This is exactly why
			// relocation keys off a marker instead of "is the target empty?".
			SetupProfiles(new UserProfile { UserId = "user-1", IdentificationNumber = "BADGE-7" });
			SetupRows(new DepartmentMemberSensitiveData
			{
				DepartmentMemberSensitiveDataId = 5,
				DepartmentId = DeptId,
				UserId = "user-1",
				IdentificationNumber = null,
				LegacyProfileRelocatedOn = DateTime.UtcNow.AddMinutes(-5)
			});

			await _service.RelocateDepartmentAsync(DeptId);

			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task A_target_holding_an_envelope_counts_as_populated()
		{
			// An enrolled department's row holds ciphertext, which is a value like any other. Writing
			// the legacy plaintext over it would destroy the protected copy.
			SetupProfiles(new UserProfile { UserId = "user-1", IdentificationNumber = "BADGE-7" });
			SetupRows(new DepartmentMemberSensitiveData
			{
				DepartmentMemberSensitiveDataId = 5,
				DepartmentId = DeptId,
				UserId = "user-1",
				IdentificationNumber = "rgdp:1:3:c29tZS1jaXBoZXJ0ZXh0",
				IsProtected = true
			});

			var result = await _service.RelocateDepartmentAsync(DeptId);

			_saved.Single().IdentificationNumber.Should().Be("rgdp:1:3:c29tZS1jaXBoZXJ0ZXh0");
			result.IdentificationNumbersMoved.Should().Be(0);

			// Still marked: this member has been through relocation, there was simply nothing to move.
			_saved.Single().LegacyProfileRelocatedOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_member_with_nothing_to_move_is_still_marked()
		{
			// Otherwise every member who never filled in an address keeps the backlog non-empty
			// forever, and the contract migration can never be cleared to run.
			SetupProfiles(new UserProfile { UserId = "user-1" });

			var result = await _service.RelocateDepartmentAsync(DeptId);

			result.MembersExamined.Should().Be(1);
			_saved.Single().LegacyProfileRelocatedOn.Should().NotBeNull();
		}

		[Test]
		public async Task One_failing_member_does_not_strand_the_rest_of_the_department()
		{
			SetupProfiles(
				new UserProfile { UserId = "user-1", IdentificationNumber = "BADGE-1" },
				new UserProfile { UserId = "user-2", IdentificationNumber = "BADGE-2" });

			_sensitiveDataService.Setup(x => x.SaveAsync(
					It.Is<DepartmentMemberSensitiveData>(d => d.UserId == "user-1"), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Protected write blocked (broker_unavailable)"));

			var result = await _service.RelocateDepartmentAsync(DeptId);

			result.Failures.Should().Be(1);
			result.MembersExamined.Should().Be(2);

			// The survivor is saved and marked; the failure never persisted a marker, so the next
			// pass retries it.
			_saved.Single().UserId.Should().Be("user-2");
		}

		[Test]
		public async Task A_missing_legacy_address_row_is_not_treated_as_a_move()
		{
			// A dangling HomeAddressId (deleted address) must leave the target empty rather than
			// writing blanks over it, and must not be counted as relocated data.
			SetupProfiles(new UserProfile { UserId = "user-1", HomeAddressId = 100 });
			_addressService.Setup(x => x.GetAddressByIdAsync(100)).ReturnsAsync((Address)null);

			var result = await _service.RelocateDepartmentAsync(DeptId);

			result.AddressesMoved.Should().Be(0);
			_saved.Single().HomeAddress1.Should().BeNull();
		}

		[Test]
		public async Task Outstanding_departments_come_back_deduplicated_and_ordered()
		{
			_repo.Setup(x => x.GetDepartmentIdsWithOutstandingLegacyProfileDataAsync())
				.ReturnsAsync(new[] { 7, 3, 7, 11 });

			var ids = await _service.GetDepartmentIdsWithOutstandingDataAsync();

			ids.Should().Equal(3, 7, 11);
		}
	}
}
