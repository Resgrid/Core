using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The GDPR export runs unattended with no protected-data grant (ADP plan 3.4 — background jobs
	/// cannot obtain user grants), and the archive it produces is stored in the database for up to
	/// seven days behind a one-time download token. Ciphertext must never reach it, and what is held
	/// back has to be declared rather than silently dropped from a subject access request.
	/// </summary>
	[TestFixture]
	public class GdprExportProtectedDataTests
	{
		private const int DeptId = 4;
		private const string UserId = "user-1";

		private Mock<IGdprDataExportRequestRepository> _repository;
		private Mock<IUserProfileService> _userProfileService;
		private Mock<IDepartmentMemberSensitiveDataService> _memberSensitiveDataService;
		private Mock<IDepartmentMemberEmergencyContactService> _emergencyContactService;
		private Mock<IUsersService> _usersService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<IActionLogsService> _actionLogsService;
		private Mock<IMessageService> _messageService;
		private Mock<ICertificationService> _certificationService;
		private Mock<ITrainingService> _trainingService;
		private Mock<IShiftsService> _shiftsService;
		private Mock<IEmailService> _emailService;
		private GdprDataExportRequest _request;
		private GdprDataExportService _service;

		[SetUp]
		public void SetUp()
		{
			_request = new GdprDataExportRequest
			{
				GdprDataExportRequestId = "req-1",
				UserId = UserId,
				DepartmentId = DeptId,
				Status = (int)GdprExportStatus.Pending,
				RequestedOn = DateTime.UtcNow
			};

			_repository = new Mock<IGdprDataExportRequestRepository>();
			_repository.Setup(x => x.GetPendingRequestsAsync())
				.ReturnsAsync(new List<GdprDataExportRequest> { _request });
			_repository.Setup(x => x.TryClaimForProcessingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_repository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<GdprDataExportRequest>(), It.IsAny<CancellationToken>(),
					It.IsAny<bool>()))
				.Returns<GdprDataExportRequest, CancellationToken, bool>((r, ct, f) => Task.FromResult(r));

			_userProfileService = new Mock<IUserProfileService>();
			_userProfileService.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "Dana", LastName = "Reed", Language = "en" });

			_memberSensitiveDataService = new Mock<IDepartmentMemberSensitiveDataService>();
			_memberSensitiveDataService.Setup(x => x.GetResolvedForDepartmentAsync(DeptId, null, UserId))
				.ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>());

			_emergencyContactService = new Mock<IDepartmentMemberEmergencyContactService>();
			_emergencyContactService.Setup(x => x.GetAllForMemberAsync(DeptId, UserId))
				.ReturnsAsync(new List<DepartmentMemberEmergencyContact>());

			_usersService = new Mock<IUsersService>();
			_usersService.Setup(x => x.GetUserById(UserId, It.IsAny<bool>()))
				.Returns(new IdentityUser { Id = UserId, Email = "dana@example.org", UserName = "dana" });

			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();

			_actionLogsService = new Mock<IActionLogsService>();
			_actionLogsService.Setup(x => x.GetAllActionLogsForUser(UserId)).ReturnsAsync(new List<ActionLog>());

			_messageService = new Mock<IMessageService>();
			_messageService.Setup(x => x.GetInboxMessagesByUserIdAsync(UserId)).ReturnsAsync(new List<Message>());
			_messageService.Setup(x => x.GetSentMessagesByUserIdAsync(UserId)).ReturnsAsync(new List<Message>());

			_certificationService = new Mock<ICertificationService>();
			_certificationService.Setup(x => x.GetCertificationsByUserIdAsync(UserId))
				.ReturnsAsync(new List<PersonnelCertification>());

			_trainingService = new Mock<ITrainingService>();
			_trainingService.Setup(x => x.GetTrainingUsersForUserAsync(UserId)).ReturnsAsync(new List<TrainingUser>());

			_shiftsService = new Mock<IShiftsService>();
			_shiftsService.Setup(x => x.GetShiftPersonsForUserAsync(UserId)).ReturnsAsync(new List<ShiftPerson>());

			_emailService = new Mock<IEmailService>();

			_service = new GdprDataExportService(_repository.Object, _userProfileService.Object,
				_memberSensitiveDataService.Object, _emergencyContactService.Object, _usersService.Object,
				_departmentsService.Object, _departmentGroupsService.Object, _personnelRolesService.Object,
				_actionLogsService.Object, _messageService.Object, _certificationService.Object,
				_trainingService.Object, _shiftsService.Object, _emailService.Object);
		}

		private async Task<Dictionary<string, string>> RunExportAsync()
		{
			await _service.ProcessPendingRequestsAsync(CancellationToken.None);

			_request.ExportData.Should().NotBeNull();

			var files = new Dictionary<string, string>(StringComparer.Ordinal);
			using var ms = new MemoryStream(_request.ExportData);
			using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
			foreach (var entry in archive.Entries)
			{
				using var stream = entry.Open();
				using var reader = new StreamReader(stream, Encoding.UTF8);
				files[entry.FullName] = await reader.ReadToEndAsync();
			}

			return files;
		}

		[Test]
		public async Task A_text_envelope_never_reaches_the_archive()
		{
			// PersonnelCertification.Name is named as protected content in the plan's Personnel
			// family. The day it enters the catalog this entry starts carrying envelopes, and there
			// is no reveal step in a background job to turn them back into values.
			_certificationService.Setup(x => x.GetCertificationsByUserIdAsync(UserId))
				.ReturnsAsync(new List<PersonnelCertification>
				{
					new PersonnelCertification
					{
						PersonnelCertificationId = 1,
						UserId = UserId,
						DepartmentId = DeptId,
						Name = "rgdp:1:2:c29tZS1jaXBoZXJ0ZXh0",
						Number = "12345"
					}
				});

			var files = await RunExportAsync();

			files["certifications.json"].Should().NotContain("rgdp:");
			files["certifications.json"].Should().Contain(ProtectedDataEnvelope.RedactionValue);

			// Non-protected values on the same row are untouched — this is a per-value redaction, not
			// a dropped record.
			files["certifications.json"].Should().Contain("12345");
		}

		[Test]
		public async Task A_binary_envelope_serialized_as_base64_never_reaches_the_archive()
		{
			// A byte[] carrying the rgdpb prefix reaches JSON as base64, so a prefix check on the
			// decoded text alone would miss it.
			var payload = Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix)
				.Concat(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }).ToArray();
			_userProfileService.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "Dana", Language = "en", Image = payload });

			var files = await RunExportAsync();

			var base64Prefix = Convert.ToBase64String(Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix));
			files["profile.json"].Should().NotContain(base64Prefix);
			files["profile.json"].Should().Contain(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task What_was_withheld_is_declared_in_a_manifest()
		{
			_certificationService.Setup(x => x.GetCertificationsByUserIdAsync(UserId))
				.ReturnsAsync(new List<PersonnelCertification>
				{
					new PersonnelCertification { PersonnelCertificationId = 1, UserId = UserId, Name = "rgdp:1:2:aaa" },
					new PersonnelCertification { PersonnelCertificationId = 2, UserId = UserId, Name = "rgdp:1:2:bbb" }
				});

			var files = await RunExportAsync();

			files.Should().ContainKey("withheld.json");
			var manifest = JObject.Parse(files["withheld.json"]);

			manifest["totalValuesWithheld"].Value<int>().Should().Be(2);
			manifest["placeholder"].Value<string>().Should().Be(ProtectedDataEnvelope.RedactionValue);
			manifest["notice"].Value<string>().Should().NotBeNullOrWhiteSpace();
			manifest["howToObtain"].Value<string>().Should().NotBeNullOrWhiteSpace();

			// Array indices collapse, so two withheld rows name the field once rather than twice.
			// Paths carry the serialized property casing, which is the casing in the archive.
			var fields = manifest["entries"]["certifications.json"]["fields"].Values<string>().ToList();
			fields.Should().ContainSingle().Which.Should().Be("[].Name");
			manifest["entries"]["certifications.json"]["valuesWithheld"].Value<int>().Should().Be(2);
		}

		[Test]
		public async Task Values_already_redacted_upstream_are_reported_too()
		{
			// Membership data is resolved through the read pipeline, so it arrives as REDACTED rather
			// than ciphertext. The manifest would be lying if it said nothing was withheld from an
			// entry the member can see gaps in.
			_memberSensitiveDataService.Setup(x => x.GetResolvedForDepartmentAsync(DeptId, null, UserId))
				.ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>
				{
					[UserId] = new DepartmentMemberSensitiveData
					{
						DepartmentId = DeptId,
						UserId = UserId,
						IdentificationNumber = ProtectedDataEnvelope.RedactionValue
					}
				});

			var files = await RunExportAsync();

			files.Should().ContainKey("withheld.json");
			var manifest = JObject.Parse(files["withheld.json"]);
			manifest["entries"]["membership.json"]["fields"].Values<string>()
				.Should().Contain(f => f.Contains("dentificationNumber"));
		}

		[Test]
		public async Task An_unprotected_department_gets_no_manifest()
		{
			var files = await RunExportAsync();

			files.Should().NotContainKey("withheld.json");
			files.Should().ContainKey("profile.json");
			files.Should().ContainKey("membership.json");
		}

		[Test]
		public async Task The_manifest_speaks_the_members_language()
		{
			_userProfileService.Setup(x => x.GetProfileByUserIdAsync(UserId, It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = UserId, FirstName = "Dana", Language = "de" });
			_certificationService.Setup(x => x.GetCertificationsByUserIdAsync(UserId))
				.ReturnsAsync(new List<PersonnelCertification>
				{
					new PersonnelCertification { PersonnelCertificationId = 1, UserId = UserId, Name = "rgdp:1:2:aaa" }
				});

			var files = await RunExportAsync();

			var manifest = JObject.Parse(files["withheld.json"]);
			manifest["notice"].Value<string>().Should().Contain("Datenschutz");
		}
	}
}
