using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// CE reporting files (§13a): the CAPCE and Florida CE Broker layouts against fixtures, the provider, CE number, status, range and
	/// former-member rules, configurable columns, formula-safe cells, the settings round trip, and the gates (officers, the add-on active
	/// or lapsed; settings only while active).
	/// </summary>
	[TestFixture]
	public class LmsCeExportTests
	{
		private const int Department = 601;
		private const string Officer = "c0ffee00-0000-4000-8000-000000000001";
		private const string Paramedic = "c0ffee00-0000-4000-8000-000000000002";
		private const string Hidden = "c0ffee00-0000-4000-8000-000000000003";
		private static readonly DateTime March = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

		private static LmsCeReportingSettings Settings() => new LmsCeReportingSettings
		{
			CapceProviderNumber = "CAPCE-1234", CapceProviderName = "Station 9 Training", LicenseCertificationTypeId = 7, NremtCertificationTypeId = 8,
			NemsidCertificationTypeId = 9, CeBrokerProviderNumber = "50-2486"
		};

		private static Dictionary<string, LmsExportMember> Members() => new Dictionary<string, LmsExportMember>(StringComparer.OrdinalIgnoreCase)
		{
			[Paramedic] = new LmsExportMember { UserId = Paramedic, FirstName = "Pat", LastName = "Medic", Email = "pat@example.org", IdentificationNumber = "E-17", IsCurrent = true, IsActive = true },
			[Hidden] = new LmsExportMember { UserId = Hidden, FirstName = "Hal", LastName = "=Hidden()", IsCurrent = true, IsActive = false }
		};

		private static LearningRecord Record(int id, string user = Paramedic, string ceNumber = "20-123", string provider = null,
			LearningRecordSourceKinds kind = LearningRecordSourceKinds.LmsCourseCompletion, LearningRecordStatuses status = LearningRecordStatuses.Verified, int day = 5) => new LearningRecord
		{
			LearningRecordId = id, DepartmentId = Department, UserId = user, SourceKind = (int)kind, Status = (int)status, Title = "Airway update " + id,
			CompletedOn = March.AddDays(day - 1).AddHours(15), Hours = 2, CeHours = 1.5m, CeCourseNumber = ceNumber, CeProvider = provider, CategoryId = 4, CourseCode = "EMS-2"
		};

		private static List<LearningRecord> Records() => new List<LearningRecord>
		{
			Record(1),                                                                         // own course, blank provider: in
			Record(2, provider: "HSI"),                                                        // a partner's CE: never in the department's CAPCE file
			Record(3, provider: "station 9 training ", kind: LearningRecordSourceKinds.ExternalSubmission), // names the provider: in
			Record(4, kind: LearningRecordSourceKinds.ExternalSubmission),                     // blank provider on an outside certificate: out
			Record(5, ceNumber: null),                                                         // no CE number: out
			Record(6, status: LearningRecordStatuses.Pending),                                 // not verified: out
			Record(7, status: LearningRecordStatuses.Voided),                                  // voided: out
			Record(8, day: 40),                                                                // after the range: out
			Record(9, user: Hidden),                                                           // hidden member: only with the switch
			new LearningRecord { LearningRecordId = 10, DepartmentId = 999, UserId = Paramedic, Status = 1, CompletedOn = March.AddDays(3), CeCourseNumber = "1" } // another department
		};

		private static Dictionary<string, LmsCeMemberIdentity> Identities() => new Dictionary<string, LmsCeMemberIdentity>(StringComparer.OrdinalIgnoreCase)
		{
			[Paramedic] = LmsCeExportRules.Identity(new[]
			{
				new PersonnelCertification { PersonnelCertificationId = 1, UserId = Paramedic, DepartmentCertificationTypeId = 7, Number = "PM12345", Area = "FL", Type = "Paramedic", ExpiresOn = new DateTime(2027, 12, 31) },
				new PersonnelCertification { PersonnelCertificationId = 2, UserId = Paramedic, DepartmentCertificationTypeId = 7, Number = "OLD-1", Area = "FL", ExpiresOn = new DateTime(2025, 12, 31) },
				new PersonnelCertification { PersonnelCertificationId = 3, UserId = Paramedic, DepartmentCertificationTypeId = 8, Number = "P1234567", ExpiresOn = new DateTime(2028, 3, 31) },
				new PersonnelCertification { PersonnelCertificationId = 4, UserId = Paramedic, DepartmentCertificationTypeId = 9, Number = "123456789012" },
				new PersonnelCertification { PersonnelCertificationId = 5, UserId = Paramedic, DepartmentCertificationTypeId = 7, Number = "DELETED", ExpiresOn = new DateTime(2030, 1, 1), IsDeleted = true }
			}, Settings())
		};

		private static Dictionary<int, TrainingCategory> Categories() => new Dictionary<int, TrainingCategory> { [4] = new TrainingCategory { TrainingCategoryId = 4, Name = "Airway", ExternalCode = "NCCP-A" } };

		private static LmsCeExportFile Build(string key, bool former = false, LmsCeReportingSettings settings = null)
		{
			settings ??= Settings();
			var exporter = LmsCeExporterCatalog.Find(key);
			var selected = LmsCeExportRules.Select(exporter, Records(), settings, March, March.AddMonths(1), Members(), former, null, Department);
			var file = LmsCeExportRules.Build(exporter, settings.ColumnsFor(exporter), selected, Members(), Identities(), settings, Categories(), utc => utc.AddHours(-5));
			file.Bytes = LmsCeExportRules.ToBytes(file);
			return file;
		}

		[Test]
		public void The_capce_file_has_the_documented_data_elements_in_order()
		{
			var file = Build(LmsCeExporterCatalog.Capce);
			var text = Encoding.UTF8.GetString(file.Bytes);
			text.Should().StartWith(
				"CAPCE Activity Number,Date of Completion,First Name,Last Name,Email,State of Licensure,State License Number,License Level,License Expiration Date," +
				"NEMSID,NREMT Number,NREMT Re-registration Date,CEH,CEH Category\r\n");
			text.Should().Contain("20-123,03/05/2026,Pat,Medic,pat@example.org,FL,PM12345,Paramedic,12/31/2027,123456789012,P1234567,03/31/2028,1.5,NCCP-A\r\n");
			file.Rows.Select(r => r[0]).Should().HaveCount(2, "the own course with a blank provider and the record naming the provider");
			file.Bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "upload files carry no byte order mark");
			LmsCeExporterCatalog.Find(LmsCeExporterCatalog.Capce).LayoutVerified.Should().BeFalse("CAPCE's column order is not public; the columns are configurable");
		}

		[Test]
		public void The_florida_ce_broker_file_follows_the_easy_excel_columns_without_a_header()
		{
			var file = Build(LmsCeExporterCatalog.FloridaEms);
			var lines = Encoding.UTF8.GetString(file.Bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
			lines.Should().HaveCount(2, "no header row: CE Broker reads column A of every row as a provider number");
			lines[0].Should().Be("2486,123,,,PM12345,3/5/2026,State=FL,");
			LmsCeExporterCatalog.Find(LmsCeExporterCatalog.FloridaEms).LayoutVerified.Should().BeTrue();
			LmsCeExportRules.Suffix("50-2486").Should().Be("2486");
			LmsCeExportRules.Suffix("ABC-1").Should().Be("ABC-1", "only a numeric prefix is a CE Broker prefix");
		}

		[Test]
		public void The_fallback_and_roster_files_have_their_default_shapes()
		{
			var state = Encoding.UTF8.GetString(Build(LmsCeExporterCatalog.VirginiaEms).Bytes).Split("\r\n")[0];
			state.Should().Be("Last Name,First Name,State License Number,Course Number,Course Title,Completion Date,CE Hours,Category,CE Provider,Instructor");

			var roster = Build(LmsCeExporterCatalog.Roster);
			Encoding.UTF8.GetString(roster.Bytes).Split("\r\n")[0].Should().Be(
				"Course Code,Course Title,Training Facility,Program Hours,Date Started,Date Ended,Last Name,First Name,Member Number,State Certification Number,Hours Attended,Method,Instructors");
			roster.Rows.Should().HaveCount(5, "the roster takes every verified record in range of active members, CE number or not, any provider");
			LmsCeExporterCatalog.All.Select(e => e.Key).Should().OnlyHaveUniqueItems().And.Contain(new[] { "capce", "fl-ems", "va-ems", "nj-ems", "fl-fire", "state-ce", "state-roster" });
		}

		[Test]
		public void Former_and_hidden_members_need_the_explicit_switch()
		{
			Build(LmsCeExporterCatalog.Capce).Rows.Should().NotContain(r => r.Contains("Hal"));
			var with = Build(LmsCeExporterCatalog.Capce, former: true);
			with.Rows.Should().Contain(r => r.Contains("Hal"));
			Encoding.UTF8.GetString(with.Bytes).Should().Contain(",'=Hidden(),", "a formula-looking name is written as text");
			with.MissingValues["State License Number"].Should().Be(1, "the hidden member has no license on file");
		}

		[Test]
		public void A_department_can_replace_and_restore_an_exporters_columns()
		{
			var settings = Settings();
			settings.ColumnMaps.Add(new LmsCeColumnMap
			{
				ExporterKey = "CAPCE",
				Columns = new List<LmsCeColumn>
				{
					new LmsCeColumn(" Activity ", LmsCeColumnSources.CeCourseNumber), new LmsCeColumn("Done", LmsCeColumnSources.CompletedOn, "yyyyMMdd"),
					new LmsCeColumn("", LmsCeColumnSources.Email), new LmsCeColumn("Bad", (LmsCeColumnSources)999), new LmsCeColumn("Fixed", LmsCeColumnSources.Constant, "X")
				}
			});
			settings = settings.Normalized();
			settings.HasCustomColumns("capce").Should().BeTrue();
			var file = Build(LmsCeExporterCatalog.Capce, settings: settings);
			file.Headers.Should().Equal(new[] { "Activity", "Done", "Fixed" }, "blank headings and unknown values are dropped");
			file.Rows[0].Should().Equal("20-123", "20260305", "X");

			settings.ColumnMaps.Clear();
			settings.ColumnsFor(LmsCeExporterCatalog.Find("capce")).Should().HaveCount(14, "no map: the default columns");
		}

		[Test]
		public void The_settings_round_trip_through_the_department_setting_store()
		{
			var settings = Settings();
			settings.ColumnMaps.Add(new LmsCeColumnMap { ExporterKey = "fl-ems", Columns = new List<LmsCeColumn> { new LmsCeColumn("A", LmsCeColumnSources.Constant, "State=FL") } });
			var back = ObjectSerialization.Deserialize<LmsCeReportingSettings>(ObjectSerialization.Serialize(settings.Normalized())).Normalized();
			back.Should().BeEquivalentTo(settings.Normalized());
			back.ColumnMaps.Single().Columns.Single().Argument.Should().Be("State=FL");
			new LmsCeReportingSettings { ColumnMaps = new List<LmsCeColumnMap> { new LmsCeColumnMap { ExporterKey = "nowhere", Columns = new List<LmsCeColumn> { new LmsCeColumn("A", 0) } } } }
				.Normalized().ColumnMaps.Should().BeEmpty("a map for an unknown exporter is dropped");
		}

		#region Service gates

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ILearningRecordService> _records;
		private Mock<ICertificationService> _certifications;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IEventAggregator> _events;
		private Mock<IDepartmentMemberSensitiveDataService> _sensitiveData;
		private Mock<IProtectedReadService> _protectedRead;
		private Mock<IProtectedGrantContext> _grant;
		private LearningRecordQuery _lastQuery;
		private string _savedSetting;

		[SetUp]
		public void SetUp()
		{
			_savedSetting = ObjectSerialization.Serialize(Settings());
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetSettingByTypeAsync(Department, DepartmentSettingTypes.LmsCeReportingSettings))
				.ReturnsAsync(() => new DepartmentSetting { DepartmentId = Department, Setting = _savedSetting });
			_settings.Setup(s => s.SaveOrUpdateSettingAsync(Department, It.IsAny<string>(), DepartmentSettingTypes.LmsCeReportingSettings, It.IsAny<CancellationToken>()))
				.Callback((int _, string value, DepartmentSettingTypes __, CancellationToken ___) => _savedSetting = value)
				.ReturnsAsync(new DepartmentSetting());
			_records = new Mock<ILearningRecordService>();
			_records.Setup(r => r.QueryAsync(It.IsAny<LearningRecordQuery>())).Callback((LearningRecordQuery q) => _lastQuery = q)
				.ReturnsAsync((LearningRecordQuery q) => q.Skip == 0 ? Records().Where(r => r.DepartmentId == Department).ToList() : new List<LearningRecord>());
			_records.Setup(r => r.GetCategoriesAsync(Department, true)).ReturnsAsync(Categories().Values.ToList());
			_certifications = new Mock<ICertificationService>();
			_certifications.Setup(c => c.GetAllCertificationTypesByDepartmentAsync(Department)).ReturnsAsync(new List<DepartmentCertificationType>
			{
				new DepartmentCertificationType { DepartmentCertificationTypeId = 7, DepartmentId = Department, Type = "State license" },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 8, DepartmentId = Department, Type = "NREMT" },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 9, DepartmentId = Department, Type = "NEMSID" }
			});
			_certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Department, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { DepartmentId = Department, UserId = Paramedic, DepartmentCertificationTypeId = 7, Number = "PM12345", Area = "FL" }
			});
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Department, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Department, TimeZone = "Eastern Standard Time" });
			_departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(Department)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = Department, UserId = Paramedic }, new DepartmentMember { DepartmentId = Department, UserId = Hidden, IsHidden = true }
			});
			_departments.Setup(d => d.GetAllUsersForDepartmentUnlimitedAsync(Department, It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetAllProfilesForDepartmentIncDisabledDeletedAsync(Department)).ReturnsAsync(new Dictionary<string, UserProfile>
			{
				[Paramedic] = new UserProfile { UserId = Paramedic, FirstName = "Pat", LastName = "Medic" }, [Hidden] = new UserProfile { UserId = Hidden, FirstName = "Hal", LastName = "Hidden" }
			});
			_events = new Mock<IEventAggregator>();
			_sensitiveData = new Mock<IDepartmentMemberSensitiveDataService>();
			_protectedRead = new Mock<IProtectedReadService>();
			_grant = new Mock<IProtectedGrantContext>();
		}

		private LmsCeExportService Service() => new LmsCeExportService(_settings.Object, _records.Object, _certifications.Object, _departments.Object, _profiles.Object, _events.Object,
			_sensitiveData.Object, _protectedRead.Object, _grant.Object);

		private static LmsActor Actor(bool admin, bool? addonActive, bool flag = true, DateTime? lastEntitled = null) => new LmsActor
		{
			DepartmentId = Department, UserId = Officer, IsCurrentMember = true, IsDepartmentAdmin = admin,
			Gate = new LmsDepartmentGate { DepartmentId = Department, ModuleOn = true, FlagOn = flag, AddonActive = addonActive, LastEntitledOn = lastEntitled }
		};

		private static LmsCeExportRequest March2026(string key = LmsCeExporterCatalog.Capce, bool former = false) =>
			new LmsCeExportRequest { ExporterKey = key, FromLocal = new DateTime(2026, 3, 1), ToLocal = new DateTime(2026, 3, 31), IncludeFormerMembers = former };

		[Test]
		public async Task Officers_build_files_while_the_add_on_is_active_or_lapsed_and_only_downloads_are_audited()
		{
			var active = await Service().BuildAsync(Actor(true, true), false, March2026(), true);
			active.Problem.Should().BeNull();
			active.Rows.Should().HaveCount(2);
			active.FileName.Should().Be("capce-completions-20260301-20260331.csv");
			_events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.LmsCeExportDownloaded)), Times.Once());

			var lapsed = await Service().BuildAsync(Actor(false, false, lastEntitled: March), true, March2026(), false);
			lapsed.Problem.Should().BeNull("records stay exportable after a lapse; a records manager may build files");
			_events.Verify(e => e.SendMessage(It.IsAny<AuditEvent>()), Times.Once(), "a preview is not audited");

			(await Service().BuildAsync(Actor(false, true), false, March2026(), true)).Problem.Should().Be("Ce.Problem.NotAuthorized", "a member who is not an officer");
			(await Service().BuildAsync(Actor(true, false), false, March2026(), true)).Problem.Should().Be("Ce.Problem.NotAuthorized", "never had the add-on");
			(await Service().BuildAsync(Actor(true, true, flag: false), false, March2026(), true)).Problem.Should().Be("Ce.Problem.NotAuthorized", "the LMS is dark");
			(await Service().BuildAsync(Actor(true, true), false, March2026("nowhere"), true)).Problem.Should().Be("Ce.Problem.UnknownExporter");
		}

		[Test]
		public async Task The_range_is_the_departments_local_days_and_completion_dates_are_local()
		{
			var file = await Service().BuildAsync(Actor(true, true), false, March2026(), false);
			_lastQuery.From.Should().Be(new DateTime(2026, 3, 1, 5, 0, 0), "midnight in New York is 05:00 UTC in March");
			_lastQuery.To.Should().Be(new DateTime(2026, 4, 1, 4, 0, 0), "the day after the last day, after the daylight-saving change");
			_lastQuery.Statuses.Should().Equal((int)LearningRecordStatuses.Verified);
			file.Rows[0][1].Should().Be("03/05/2026", "15:00 UTC is 10:00 in New York the same day");
			file.Rows[0][6].Should().Be("PM12345", "the license number comes from the chosen certification type");
		}

		[Test]
		public async Task Adp_a_protected_departments_licence_and_member_numbers_leave_redacted_without_a_grant_and_revealed_with_one()
		{
			// Round 8: a licence number (PersonnelCertifications.Number) and the member's identification number are protected personnel data.
			// A CSV download carries no grant, so they are withheld exactly as the reports and certification pages withhold them.
			using var kit = new Resgrid.Tests.Adp.AdpTestKit(departmentId: Department);
			var cert = new PersonnelCertification { PersonnelCertificationId = 31, DepartmentId = Department, UserId = Paramedic, DepartmentCertificationTypeId = 7, Area = "FL" };
			cert.Number = kit.Seal("personnelcertifications.number", "31", "PM-SECRET-777");
			_certifications.Setup(c => c.GetCertificationsForDepartmentAsync(Department, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync(() => new List<PersonnelCertification> { new PersonnelCertification { PersonnelCertificationId = 31, DepartmentId = Department, UserId = Paramedic, DepartmentCertificationTypeId = 7, Area = "FL", Number = cert.Number } });
			_protectedRead = new Mock<IProtectedReadService>();
			IProtectedReadService seam = kit.Seam;

			LmsCeExportService Build(bool withGrant) => new LmsCeExportService(_settings.Object, _records.Object, _certifications.Object, _departments.Object, _profiles.Object,
				_events.Object, _sensitiveData.Object, seam, kit.Attended(withGrant, Officer));

			var withheld = await Build(false).BuildAsync(Actor(true, true), false, March2026(), false);
			withheld.Problem.Should().BeNull("a protected department still exports");
			withheld.Rows[0][6].Should().Be(ProtectedDataEnvelope.RedactionValue);
			Encoding.UTF8.GetString(withheld.Bytes).Should().NotContain("PM-SECRET-777").And.NotContain("rgdp:");
			_sensitiveData.Verify(s => s.ApplyIdentificationNumbersAsync(Department, It.IsAny<IEnumerable<UserProfile>>(), null, It.IsAny<string>()), Times.Once(),
				"the member number is stamped from the department-scoped row through the protected pipeline");

			var revealed = await Build(true).BuildAsync(Actor(true, true), false, March2026(), false);
			revealed.Rows[0][6].Should().Be("PM-SECRET-777", "a grant holder (the reveal module's download) gets the number");
		}

		[Test]
		public async Task The_capce_file_needs_the_provider_number()
		{
			_savedSetting = ObjectSerialization.Serialize(new LmsCeReportingSettings());
			(await Service().BuildAsync(Actor(true, true), false, March2026(), true)).Problem.Should().Be("Ce.Problem.NoCapceProvider");
			(await Service().BuildAsync(Actor(true, true), false, March2026(LmsCeExporterCatalog.FloridaEms), true)).Problem.Should().Be("Ce.Problem.NoCeBrokerProvider");
			(await Service().BuildAsync(Actor(true, true), false, March2026(LmsCeExporterCatalog.Roster), true)).Problem.Should().BeNull("the roster needs no provider number");
		}

		[Test]
		public async Task Settings_change_only_while_the_add_on_is_active_and_name_this_departments_types()
		{
			var service = Service();
			service.CanConfigure(Actor(true, false, lastEntitled: March)).Should().BeFalse("lapse rule 3: nothing new while lapsed");
			service.CanConfigure(Actor(false, true)).Should().BeFalse("department admins only");
			Func<Task> lapsed = () => service.SaveSettingsAsync(Actor(true, false, lastEntitled: March), Settings());
			await lapsed.Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_ce_not_authorized");

			Func<Task> foreign = () => service.SaveSettingsAsync(Actor(true, true), new LmsCeReportingSettings { CapceProviderNumber = "X", LicenseCertificationTypeId = 4242 });
			await foreign.Should().ThrowAsync<InvalidOperationException>().WithMessage("lms_ce_certification_type_invalid");

			await service.SaveColumnsAsync(Actor(true, true), "va-ems", new[] { new LmsCeColumn("Name", LmsCeColumnSources.FullName) });
			var saved = await service.SaveSettingsAsync(Actor(true, true), new LmsCeReportingSettings { CapceProviderNumber = "  CAPCE-9 ", LicenseCertificationTypeId = 7 });
			saved.CapceProviderNumber.Should().Be("CAPCE-9");
			saved.HasCustomColumns("va-ems").Should().BeTrue("the settings form keeps the column maps");
			(await service.GetSettingsAsync(Department)).CapceProviderNumber.Should().Be("CAPCE-9");

			await service.SaveColumnsAsync(Actor(true, true), "va-ems", null);
			(await service.GetSettingsAsync(Department)).HasCustomColumns("va-ems").Should().BeFalse("an empty list restores the defaults");
			_events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.LmsCeReportingSettingsChanged)), Times.Exactly(3));
		}

		#endregion Service gates
	}
}
