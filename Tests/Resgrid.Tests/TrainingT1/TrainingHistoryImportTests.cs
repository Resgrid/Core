using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Keys = Resgrid.Services.TrainingHistoryImportCsv.Keys;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>
	/// History import (R-C1): the parser matches columns by name and alias in any order and reads the file's number
	/// convention; members match by email, user id, the department's id number and an exact unique name, former members
	/// included, and an officer's pick wins; a dry run writes nothing and flags rows already imported or repeated in the file;
	/// the import writes Import, backfill (never auto-credited) records under a stable source ref and adds the certification
	/// credit the row names once.
	/// </summary>
	[TestFixture]
	public class TrainingHistoryImportTests
	{
		private const int DepartmentId = 42;
		private const string Officer = "officer";

		private Mock<ILearningRecordWriter> _writer;
		private Mock<ILearningRecordsRepository> _repository;
		private Mock<ILearningRecordService> _learningRecords;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentMemberSensitiveDataService> _sensitive;
		private Mock<ICertificationService> _certifications;
		private TrainingHistoryImportService _service;

		private List<(LearningRecordSourceKinds Kind, string SourceRefId, LearningRecordDraft Draft, string ActingUserId)> _writes;
		private Func<LearningRecordDraft, LearningRecordWriteResult> _onWrite;
		private HashSet<string> _existingRefs;
		private List<IEnumerable<string>> _askedRefs;

		[SetUp]
		public void SetUp()
		{
			_writer = new Mock<ILearningRecordWriter>();
			_repository = new Mock<ILearningRecordsRepository>();
			_learningRecords = new Mock<ILearningRecordService>();
			_departments = new Mock<IDepartmentsService>();
			_profiles = new Mock<IUserProfileService>();
			_sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
			_certifications = new Mock<ICertificationService>();
			_writes = new List<(LearningRecordSourceKinds, string, LearningRecordDraft, string)>();
			_existingRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_askedRefs = new List<IEnumerable<string>>();
			_onWrite = null;

			_departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "u-jane" },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "u-bob", IsDeleted = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "u-ann", IsDisabled = true, IsHidden = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "u-chris1" },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "u-chris2" },
				new DepartmentMember { DepartmentId = 99, UserId = "u-elsewhere" }
			});

			_profiles.Setup(p => p.GetAllProfilesForDepartmentIncDisabledDeletedAsync(DepartmentId)).ReturnsAsync(new Dictionary<string, UserProfile>
			{
				["u-jane"] = new UserProfile { UserId = "u-jane", FirstName = "Jane", LastName = "Doe", MembershipEmail = "jane@example.com" },
				["u-bob"] = new UserProfile { UserId = "u-bob", FirstName = "Bob", LastName = "Smith", MembershipEmail = "bob@example.com" },
				["u-ann"] = new UserProfile { UserId = "u-ann", FirstName = "Ann", LastName = "Lee", MembershipEmail = "ann@example.com" },
				["u-chris1"] = new UserProfile { UserId = "u-chris1", FirstName = "Chris", LastName = "Kim", MembershipEmail = "chris1@example.com" },
				["u-chris2"] = new UserProfile { UserId = "u-chris2", FirstName = "Chris", LastName = "Kim", MembershipEmail = "chris2@example.com" }
			});

			_sensitive.Setup(s => s.GetResolvedForDepartmentAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>(StringComparer.OrdinalIgnoreCase)
				{
					["u-jane"] = new DepartmentMemberSensitiveData { UserId = "u-jane", IdentificationNumber = "E100" },
					["u-bob"] = new DepartmentMemberSensitiveData { UserId = "u-bob", IdentificationNumber = "E200" },
					["u-ann"] = new DepartmentMemberSensitiveData { UserId = "u-ann", IdentificationNumber = ProtectedDataEnvelope.RedactionValue }
				});

			_learningRecords.Setup(l => l.GetCategoriesAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory>
			{
				new TrainingCategory { TrainingCategoryId = 3, DepartmentId = DepartmentId, Code = "ISO-CO", Name = "Company training" },
				new TrainingCategory { TrainingCategoryId = 4, DepartmentId = DepartmentId, Code = "HAZ", Name = "Hazmat" },
				new TrainingCategory { TrainingCategoryId = 5, DepartmentId = DepartmentId, Code = "OLD", Name = "Old", IsDeleted = true }
			});

			_repository.Setup(r => r.GetExistingSourceRefIdsAsync(DepartmentId, It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
				.Returns((int departmentId, int kind, IEnumerable<string> refs) =>
				{
					var list = refs.ToList();
					_askedRefs.Add(list);
					return Task.FromResult(new HashSet<string>(list.Where(_existingRefs.Contains), StringComparer.OrdinalIgnoreCase));
				});

			_writer.Setup(w => w.WriteSourceAsync(It.IsAny<int>(), It.IsAny<LearningRecordSourceKinds>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((int departmentId, LearningRecordSourceKinds kind, string sourceRefId, IReadOnlyCollection<LearningRecordDraft> drafts, string actingUserId,
					CancellationToken cancellationToken) =>
				{
					var draft = drafts.Single();
					_writes.Add((kind, sourceRefId, draft, actingUserId));
					var result = _onWrite?.Invoke(draft) ?? new LearningRecordWriteResult
					{
						Written = 1,
						Records = { new LearningRecord { LearningRecordId = 500 + _writes.Count, DepartmentId = departmentId, UserId = draft.UserId, Hours = draft.Hours, Status = (int)LearningRecordStatuses.Verified } }
					};
					return Task.FromResult(result);
				});

			_certifications.Setup(c => c.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentCertificationType>
			{
				new DepartmentCertificationType { DepartmentCertificationTypeId = 7, DepartmentId = DepartmentId, Type = "EMT-Basic", Code = "EMT", AppliesTo = (int)CertificationAppliesTo.Person },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 8, DepartmentId = DepartmentId, Type = "Retired", Code = "OLD", IsDeleted = true },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 9, DepartmentId = DepartmentId, Type = "Pump inspection", Code = "PUMP", AppliesTo = (int)CertificationAppliesTo.Unit }
			});
			_certifications.Setup(c => c.GetCertificationCreditsAsync(It.IsAny<int>())).ReturnsAsync(new List<PersonnelCertificationCredit>());
			_certifications.Setup(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertificationCredit credit, string userId, CancellationToken token) => credit);

			_service = new TrainingHistoryImportService(_writer.Object, _repository.Object, _learningRecords.Object, _departments.Object, _profiles.Object,
				_sensitive.Object, _certifications.Object);
		}

		private static byte[] File(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");

		private static TrainingHistoryImportRow Row(TrainingHistoryImportResult result, int rowNumber) => result.Rows.Single(r => r.RowNumber == rowNumber);

		private void MemberHasCertifications(params PersonnelCertification[] certifications)
		{
			_certifications.Setup(c => c.GetCertificationsForDepartmentAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int departmentId, IEnumerable<string> userIds) => certifications.Where(c => userIds == null || userIds.Contains(c.UserId)).ToList());
		}

		#region Parser

		[Test]
		public void Parse_matches_aliases_in_any_order_and_reads_semicolon_files_with_comma_decimals()
		{
			var parse = TrainingHistoryImportCsv.Parse(
				"Completion Date;E-mail;Course Name;Credit Hours;Shoe Size;CEU Hours;Trainer\r\n2025-03-14;jane@example.com;Pump ops;1,5;42;0,75;Capt. Lee\r\n",
				CultureInfo.InvariantCulture);

			parse.FileError.Should().BeNull();
			parse.IgnoredColumns.Should().Equal("Shoe Size");
			var row = parse.Rows.Single();
			row.RowNumber.Should().Be(2);
			row.Email.Should().Be("jane@example.com");
			row.Course.Should().Be("Pump ops");
			row.Hours.Should().Be(1.5m);
			row.CeHours.Should().Be(0.75m);
			row.Instructor.Should().Be("Capt. Lee");
			row.CompletedOn.Should().Be(new DateTime(2025, 3, 14, 12, 0, 0, DateTimeKind.Utc));
			row.CompletedOn.Value.Kind.Should().Be(DateTimeKind.Utc);
			row.Errors.Should().BeEmpty();
		}

		[Test]
		public void Parse_reads_comma_files_quoted_values_utc_times_and_method_words()
		{
			var parse = TrainingHistoryImportCsv.Parse(
				"Employee ID,Training,Date,Duration,Delivery,Course Number,Completion Id\r\nE100,\"Ladders, advanced\",2025-03-14T18:30:00Z,2.25,on the job,LAD-2,X-1\r\n",
				CultureInfo.InvariantCulture);

			parse.FileError.Should().BeNull();
			var row = parse.Rows.Single();
			row.MemberExternalId.Should().Be("E100");
			row.Course.Should().Be("Ladders, advanced");
			row.CourseCode.Should().Be("LAD-2");
			row.RecordId.Should().Be("X-1");
			row.CompletedOn.Should().Be(new DateTime(2025, 3, 14, 18, 30, 0, DateTimeKind.Utc));
			row.Hours.Should().Be(2.25m);
			TrainingHistoryImportCsv.MethodFor(row.Method).Should().Be(MethodsOfTraining.OnTheJob);
		}

		[TestCase("classroom", MethodsOfTraining.Classroom)]
		[TestCase("Online", MethodsOfTraining.Online)]
		[TestCase("drill", MethodsOfTraining.DrillField)]
		[TestCase("Field", MethodsOfTraining.DrillField)]
		[TestCase("DrillField", MethodsOfTraining.DrillField)]
		[TestCase("virtual", MethodsOfTraining.Virtual)]
		[TestCase("self-study", MethodsOfTraining.SelfStudy)]
		[TestCase("On the Job", MethodsOfTraining.OnTheJob)]
		[TestCase("3", MethodsOfTraining.Virtual)]
		public void Method_words_and_names_map_to_methods_of_training(string text, MethodsOfTraining expected)
		{
			TrainingHistoryImportCsv.MethodFor(text).Should().Be(expected);
		}

		[TestCase("Email,Date,Hours", Keys.FileCourseColumnRequired)]
		[TestCase("Email,Course,Hours", Keys.FileDateColumnRequired)]
		[TestCase("Email,Course,Date", Keys.FileHoursColumnRequired)]
		[TestCase("Course,Date,Hours,Notes", Keys.FileMemberColumnRequired)]
		[TestCase("Email,E-mail,Course,Date,Hours", Keys.FileDuplicateColumn)]
		public void Parse_refuses_a_file_without_a_required_column(string header, string expected)
		{
			var parse = TrainingHistoryImportCsv.Parse(File(header, "a@example.com,x,2025-01-01,1"), CultureInfo.InvariantCulture);

			parse.FileError.Should().Be(expected);
			parse.Rows.Should().BeEmpty();
		}

		[Test]
		public void Parse_refuses_empty_oversized_header_only_and_overlong_files()
		{
			TrainingHistoryImportCsv.Parse((byte[])null).FileError.Should().Be(Keys.FileInvalid);
			TrainingHistoryImportCsv.Parse(new byte[TrainingHistoryImportCsv.MaxFileBytes + 1]).FileError.Should().Be(Keys.FileTooLarge);
			TrainingHistoryImportCsv.Parse(File("Email,Course,Date,Hours")).FileError.Should().Be(Keys.FileEmpty);

			var text = new StringBuilder("Email,Course,Date,Hours\r\n");
			for (var i = 0; i <= TrainingHistoryImportCsv.MaxRows; i++)
				text.Append("a@example.com,c,2025-01-01,1\r\n");
			TrainingHistoryImportCsv.Parse(text.ToString()).FileError.Should().Be(Keys.FileTooManyRows);
		}

		[Test]
		public void Parse_numbers_rows_like_a_spreadsheet_and_reports_row_problems()
		{
			var parse = TrainingHistoryImportCsv.Parse(File(
				"Email,Course,Date,Hours,CE Hours",
				"jane@example.com,,2025-01-01,1,",
				"jane@example.com,A,,1,",
				"",
				"jane@example.com,B,31/31/2025,1,",
				"jane@example.com,C,2025-01-01,-1,",
				"jane@example.com,D,2025-01-01,1000,",
				"jane@example.com,E,2025-01-01,abc,x",
				"jane@example.com,F,2025-01-01,1,2,extra",
				"jane@example.com,G,2025-01-01,0,"), CultureInfo.InvariantCulture);

			parse.FileError.Should().BeNull();
			parse.Rows.Select(r => r.RowNumber).Should().Equal(2, 3, 5, 6, 7, 8, 9, 10);
			parse.Rows[0].Errors.Should().Equal(Keys.CourseRequired);
			parse.Rows[1].Errors.Should().Equal(Keys.DateRequired);
			parse.Rows[2].Errors.Should().Equal(Keys.DateInvalid);
			parse.Rows[3].Errors.Should().Equal(Keys.HoursInvalid);
			parse.Rows[4].Errors.Should().Equal(Keys.HoursInvalid);
			parse.Rows[5].Errors.Should().Equal(Keys.HoursInvalid);
			parse.Rows[5].Warnings.Should().Equal(Keys.CeHoursInvalid);
			parse.Rows[6].Errors.Should().Equal(Keys.Unreadable);
			parse.Rows[7].Errors.Should().BeEmpty();
			parse.Rows[7].Hours.Should().Be(0m);
		}

		[Test]
		public void Template_is_utf8_with_a_bom_and_its_example_row_imports_cleanly()
		{
			var bytes = _service.Template();

			bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
			var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
			text.Should().StartWith(string.Join(",", TrainingHistoryImportCsv.Header.Select(h => "\"" + h + "\"")) + "\r\n");

			var parse = TrainingHistoryImportCsv.Parse(bytes, CultureInfo.InvariantCulture);
			parse.FileError.Should().BeNull();
			parse.IgnoredColumns.Should().BeEmpty();
			var row = parse.Rows.Single();
			row.Errors.Should().BeEmpty();
			row.Warnings.Should().BeEmpty();
			row.CertificationIssuedOn.Should().Be(new DateTime(2024, 6, 1));
			TrainingHistoryImportCsv.MethodFor(row.Method).Should().Be(MethodsOfTraining.DrillField);
		}

		#endregion Parser

		#region Matching and dry run

		[Test]
		public async Task Dry_run_matches_members_by_email_id_external_id_and_unique_name_including_former_members()
		{
			var result = await _service.DryRunAsync(DepartmentId, File(
				"Email,MemberId,EmployeeId,Name,Course,Date,Hours",
				"JANE@Example.com,,,,A,2025-01-01,1",
				",U-BOB,,,B,2025-01-01,1",
				",,E200,,C,2025-01-01,1",
				",,,  ann   LEE ,D,2025-01-01,1",
				",,,Chris Kim,E,2025-01-01,1",
				",,REDACTED,,F,2025-01-01,1",
				",,,\"Lee, Ann\",G,2025-01-01,1",
				"nobody@example.com,,,,H,2025-01-01,1",
				",u-elsewhere,,,I,2025-01-01,1"), null);

			result.DryRun.Should().BeTrue();
			result.FileError.Should().BeNull();

			Row(result, 2).MatchMethod.Should().Be(TrainingImportMatchMethods.Email);
			Row(result, 2).MatchedUserId.Should().Be("u-jane");
			Row(result, 2).MatchedName.Should().Be("Jane Doe");

			Row(result, 3).MatchMethod.Should().Be(TrainingImportMatchMethods.MemberId);
			Row(result, 3).MatchedUserId.Should().Be("u-bob");

			Row(result, 4).MatchMethod.Should().Be(TrainingImportMatchMethods.ExternalId);
			Row(result, 4).MatchedUserId.Should().Be("u-bob");

			Row(result, 5).MatchMethod.Should().Be(TrainingImportMatchMethods.Name);
			Row(result, 5).MatchedUserId.Should().Be("u-ann");
			Row(result, 5).Warnings.Should().Contain(Keys.NameMatch);

			// Two members share the name: no guess.
			Row(result, 6).MatchedUserId.Should().BeNull();
			Row(result, 6).Errors.Should().Contain(Keys.MemberNotFound);

			// A protected id number the import cannot read never matches its placeholder.
			Row(result, 7).Errors.Should().Contain(Keys.MemberNotFound);

			Row(result, 8).MatchedUserId.Should().Be("u-ann");
			Row(result, 8).MatchMethod.Should().Be(TrainingImportMatchMethods.Name);

			Row(result, 9).Errors.Should().Equal(Keys.MemberNotFound);
			// A member of another department is not a member here.
			Row(result, 10).Errors.Should().Equal(Keys.MemberNotFound);

			result.Matched.Should().Be(5);
			result.Unmatched.Should().Be(4);
			_writer.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Officer_pick_wins_but_only_for_a_member_of_the_department()
		{
			var file = File(
				"Email,Course,Date,Hours",
				"jane@example.com,A,2025-01-01,1",
				"nobody@example.com,B,2025-01-01,1",
				"nobody@example.com,C,2025-01-01,1");

			var result = await _service.DryRunAsync(DepartmentId, file, new Dictionary<int, string> { [2] = "U-ANN", [3] = "u-bob", [4] = "u-stranger" });

			Row(result, 2).MatchMethod.Should().Be(TrainingImportMatchMethods.Manual);
			Row(result, 2).MatchedUserId.Should().Be("u-ann");
			Row(result, 3).MatchMethod.Should().Be(TrainingImportMatchMethods.Manual);
			Row(result, 3).MatchedUserId.Should().Be("u-bob");
			Row(result, 3).Errors.Should().BeEmpty();
			Row(result, 4).MatchedUserId.Should().BeNull();
			Row(result, 4).Errors.Should().Equal(Keys.MemberNotFound);
		}

		[Test]
		public async Task Categories_match_by_code_or_name_and_an_unknown_one_lands_uncategorized_with_a_warning()
		{
			var result = await _service.DryRunAsync(DepartmentId, File(
				"Email,Course,Date,Hours,Category,Method",
				"jane@example.com,A,2025-01-01,1,iso-co,",
				"jane@example.com,B,2025-01-01,1,HAZMAT,classroom",
				"jane@example.com,C,2025-01-01,1,Basket weaving,teleportation",
				"jane@example.com,D,2025-01-01,1,old,"), null);

			Row(result, 2).MatchedCategoryId.Should().Be(3);
			Row(result, 3).MatchedCategoryId.Should().Be(4);
			Row(result, 4).MatchedCategoryId.Should().BeNull();
			Row(result, 4).Warnings.Should().BeEquivalentTo(new[] { Keys.UnknownCategory, Keys.UnknownMethod });
			Row(result, 4).IsImportable.Should().BeTrue();
			// A deleted category is not offered.
			Row(result, 5).MatchedCategoryId.Should().BeNull();
			Row(result, 5).Warnings.Should().Equal(Keys.UnknownCategory);
		}

		[Test]
		public async Task Unknown_deleted_or_unit_certification_types_warn_and_are_not_matched()
		{
			var result = await _service.DryRunAsync(DepartmentId, File(
				"Email,Course,Date,Hours,Certification",
				"jane@example.com,A,2025-01-01,1,emt",
				"jane@example.com,B,2025-01-01,1,EMT-Basic",
				"jane@example.com,C,2025-01-01,1,OLD",
				"jane@example.com,D,2025-01-01,1,PUMP",
				"jane@example.com,E,2025-01-01,1,Astronaut"), null);

			Row(result, 2).MatchedCertificationTypeId.Should().Be(7);
			Row(result, 3).MatchedCertificationTypeId.Should().Be(7);
			foreach (var number in new[] { 4, 5, 6 })
			{
				Row(result, number).MatchedCertificationTypeId.Should().BeNull();
				Row(result, number).Warnings.Should().Equal(Keys.UnknownCertification);
				Row(result, number).IsImportable.Should().BeTrue();
			}
		}

		[Test]
		public async Task Future_dates_are_refused()
		{
			var result = await _service.DryRunAsync(DepartmentId, File("Email,Course,Date,Hours", "jane@example.com,A,2099-01-01,1"), null);

			result.Rows.Single().Errors.Should().Equal(Keys.FutureDate);
			result.Importable.Should().Be(0);
		}

		[Test]
		public async Task Dry_run_writes_nothing_and_flags_rows_already_imported_or_repeated_in_the_file()
		{
			_existingRefs.Add("ext:LMS-1");

			var result = await _service.DryRunAsync(DepartmentId, File(
				"Email,Course,Date,Hours,Record Id,Certification,Certification Number",
				"jane@example.com,Pump ops,2025-01-01,1,,EMT,E-1",
				"jane@example.com,pump OPS,2025-01-01,1.00,,EMT,E-1",
				"bob@example.com,Ladders,2025-01-02,2,LMS-1,,",
				"ann@example.com,Ropes,2025-01-03,2,,,"), null);

			Row(result, 2).AlreadyImported.Should().BeFalse();
			Row(result, 3).AlreadyImported.Should().BeTrue();
			Row(result, 3).Warnings.Should().Contain(Keys.DuplicateRow);
			Row(result, 4).AlreadyImported.Should().BeTrue();
			Row(result, 5).AlreadyImported.Should().BeFalse();
			result.Importable.Should().Be(2);

			_repository.Verify(r => r.GetExistingSourceRefIdsAsync(DepartmentId, (int)LearningRecordSourceKinds.Import, It.IsAny<IEnumerable<string>>()), Times.Once());
			_askedRefs.Single().Should().Contain("ext:LMS-1").And.HaveCount(3);
			_writer.VerifyNoOtherCalls();
			_certifications.Verify(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()), Times.Never());
			_certifications.Verify(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public void Source_ref_is_stable_for_the_same_row_and_uses_the_source_record_id_when_there_is_one()
		{
			TrainingHistoryImportRow Parsed(string line) =>
				TrainingHistoryImportCsv.Parse(File("Email,Course,Code,Date,Hours,Record Id", line), CultureInfo.InvariantCulture).Rows.Single();

			var first = TrainingHistoryImportService.SourceRefIdFor("u-jane", Parsed("jane@example.com,Pump ops,PO-1,2025-03-14,1.5,"));
			var again = TrainingHistoryImportService.SourceRefIdFor("U-JANE", Parsed("other@example.com,PUMP OPS ,po-1,2025-03-14,1.50,"));
			var otherHours = TrainingHistoryImportService.SourceRefIdFor("u-jane", Parsed("jane@example.com,Pump ops,PO-1,2025-03-14,2,"));
			var otherMember = TrainingHistoryImportService.SourceRefIdFor("u-bob", Parsed("jane@example.com,Pump ops,PO-1,2025-03-14,1.5,"));

			first.Should().StartWith("row:").And.HaveLength(44);
			first.Substring(4).Should().MatchRegex("^[0-9a-f]{40}$");
			again.Should().Be(first);
			otherHours.Should().NotBe(first);
			otherMember.Should().NotBe(first);

			TrainingHistoryImportService.SourceRefIdFor("u-jane", Parsed("jane@example.com,Pump ops,PO-1,2025-03-14,1.5, LMS-77 ")).Should().Be("ext:LMS-77");
			TrainingHistoryImportService.SourceRefIdFor("u-jane", new TrainingHistoryImportRow { RecordId = new string('x', 150) }).Should().Be("ext:" + new string('x', 100));
		}

		#endregion Matching and dry run

		#region Import

		[Test]
		public async Task Import_writes_verified_backfill_records_of_source_kind_import_under_the_stable_ref()
		{
			var file = File(
				"Email,Course,Course Code,Date,Hours,Category,Method,Instructor,Notes,CE Provider,CE Number,CE Hours,Record Id",
				"jane@example.com,Pump ops,PO-1,2025-03-14,1.5,ISO-CO,classroom,Capt. Lee,Annual,State Academy,CE-9,1,",
				"bob@example.com,Ladders,,2025-03-15,2,,,,,,,,LMS-9",
				"nobody@example.com,Ropes,,2025-03-15,2,,,,,,,,");

			var result = await _service.ImportAsync(DepartmentId, file, null, Officer);

			result.DryRun.Should().BeFalse();
			result.RecordsWritten.Should().Be(2);
			result.RecordsSkipped.Should().Be(1);
			_writes.Should().HaveCount(2);
			_writes.Should().OnlyContain(w => w.Kind == LearningRecordSourceKinds.Import && w.ActingUserId == Officer);

			var jane = _writes[0];
			jane.SourceRefId.Should().Be(TrainingHistoryImportService.SourceRefIdFor("u-jane", Row(result, 2)));
			jane.SourceRefId.Should().StartWith("row:");
			jane.Draft.UserId.Should().Be("u-jane");
			jane.Draft.Title.Should().Be("Pump ops");
			jane.Draft.CourseCode.Should().Be("PO-1");
			jane.Draft.CompletedOn.Should().Be(new DateTime(2025, 3, 14, 12, 0, 0, DateTimeKind.Utc));
			jane.Draft.Hours.Should().Be(1.5m);
			jane.Draft.CategoryId.Should().Be(3);
			jane.Draft.MethodOfTraining.Should().Be((int)MethodsOfTraining.Classroom);
			jane.Draft.InstructorNames.Should().Be("Capt. Lee");
			jane.Draft.Notes.Should().Be("Annual");
			jane.Draft.CeProvider.Should().Be("State Academy");
			jane.Draft.CeCourseNumber.Should().Be("CE-9");
			jane.Draft.CeHours.Should().Be(1m);
			jane.Draft.Status.Should().Be(LearningRecordStatuses.Verified);
			jane.Draft.IsBackfill.Should().BeTrue();
			jane.Draft.VerifiedByUserId.Should().Be(Officer);

			var bob = _writes[1];
			bob.SourceRefId.Should().Be("ext:LMS-9");
			bob.Draft.UserId.Should().Be("u-bob");
			bob.Draft.ExternalId.Should().Be("LMS-9");
			bob.Draft.IsBackfill.Should().BeTrue();

			// The same file again gives the same refs.
			_writes.Clear();
			await _service.ImportAsync(DepartmentId, file, null, Officer);
			_writes.Select(w => w.SourceRefId).Should().Equal(jane.SourceRefId, "ext:LMS-9");
		}

		[Test]
		public async Task Rows_already_imported_are_skipped_on_import()
		{
			_existingRefs.Add("ext:LMS-9");

			var result = await _service.ImportAsync(DepartmentId, File("Email,Course,Date,Hours,Record Id,Certification", "bob@example.com,Ladders,2025-03-15,2,LMS-9,EMT"), null, Officer);

			result.RecordsWritten.Should().Be(0);
			result.RecordsSkipped.Should().Be(1);
			_writer.VerifyNoOtherCalls();
			_certifications.Verify(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public async Task A_failing_row_is_logged_and_skipped_without_stopping_the_import()
		{
			_onWrite = draft => draft.Title == "Broken" ? throw new InvalidOperationException("boom") : null;

			var result = await _service.ImportAsync(DepartmentId, File(
				"Email,Course,Date,Hours",
				"jane@example.com,Broken,2025-03-14,1",
				"jane@example.com,Fine,2025-03-14,1"), null, Officer);

			result.RecordsWritten.Should().Be(1);
			result.RecordsSkipped.Should().Be(1);
			_writes.Select(w => w.Draft.Title).Should().Equal("Broken", "Fine");
		}

		[Test]
		public async Task Import_credits_the_certification_cycle_that_contains_the_date_with_the_learning_record_id()
		{
			MemberHasCertifications(
				new PersonnelCertification { PersonnelCertificationId = 69, DepartmentId = DepartmentId, UserId = "u-jane", DepartmentCertificationTypeId = 7, RecievedOn = new DateTime(2022, 1, 1), ExpiresOn = new DateTime(2023, 12, 31) },
				new PersonnelCertification { PersonnelCertificationId = 70, DepartmentId = DepartmentId, UserId = "u-jane", DepartmentCertificationTypeId = 7, RecievedOn = new DateTime(2024, 1, 1), ExpiresOn = new DateTime(2026, 1, 1) },
				new PersonnelCertification { PersonnelCertificationId = 71, DepartmentId = DepartmentId, UserId = "u-jane", DepartmentCertificationTypeId = 7, RecievedOn = new DateTime(2024, 1, 1), IsDeleted = true });
			_onWrite = draft => new LearningRecordWriteResult
			{
				Written = 1,
				Records = { new LearningRecord { LearningRecordId = 555, DepartmentId = DepartmentId, UserId = draft.UserId, Hours = draft.Hours, Status = (int)LearningRecordStatuses.Verified } }
			};
			PersonnelCertificationCredit added = null;
			_certifications.Setup(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), Officer, It.IsAny<CancellationToken>()))
				.Callback((PersonnelCertificationCredit credit, string userId, CancellationToken token) => added = credit)
				.ReturnsAsync((PersonnelCertificationCredit credit, string userId, CancellationToken token) => credit);

			var result = await _service.ImportAsync(DepartmentId, File(
				"Email,Course,Date,Hours,Category,Certification",
				"jane@example.com,EMT refresher,2025-03-14,3,ISO-CO,emt"), null, Officer);

			result.RecordsWritten.Should().Be(1);
			result.CreditsCreated.Should().Be(1);
			result.CertificationsCreated.Should().Be(0);
			added.Should().NotBeNull();
			added.PersonnelCertificationId.Should().Be(70);
			added.DepartmentId.Should().Be(DepartmentId);
			added.LearningRecordId.Should().Be(555);
			added.Hours.Should().Be(3m);
			added.CreditDate.Date.Should().Be(new DateTime(2025, 3, 14));
			added.Category.Should().Be("Company training");
			added.Description.Should().Be("EMT refresher");
			_certifications.Verify(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public async Task Re_importing_does_not_add_the_credit_twice()
		{
			MemberHasCertifications(new PersonnelCertification { PersonnelCertificationId = 70, DepartmentId = DepartmentId, UserId = "u-jane", DepartmentCertificationTypeId = 7, RecievedOn = new DateTime(2024, 1, 1) });
			// The record is already on the transcript (the writer leaves it unchanged) and already credits the certification.
			_onWrite = draft => new LearningRecordWriteResult
			{
				Unchanged = 1,
				Records = { new LearningRecord { LearningRecordId = 555, DepartmentId = DepartmentId, UserId = draft.UserId, Hours = draft.Hours, Status = (int)LearningRecordStatuses.Verified } }
			};
			_certifications.Setup(c => c.GetCertificationCreditsAsync(70)).ReturnsAsync(new List<PersonnelCertificationCredit>
			{
				new PersonnelCertificationCredit { PersonnelCertificationCreditId = 1, PersonnelCertificationId = 70, LearningRecordId = 555, Hours = 3m }
			});

			var result = await _service.ImportAsync(DepartmentId, File("Email,Course,Date,Hours,Certification", "jane@example.com,EMT refresher,2025-03-14,3,EMT"), null, Officer);

			result.RecordsWritten.Should().Be(0);
			result.RecordsSkipped.Should().Be(1);
			result.CreditsCreated.Should().Be(0);
			_certifications.Verify(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
		}

		[Test]
		public async Task Import_creates_the_certification_record_once_when_no_cycle_contains_the_date()
		{
			MemberHasCertifications();
			var saved = new List<PersonnelCertification>();
			_certifications.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertification certification, ImportEffectPolicy effects, CancellationToken token) =>
				{
					effects.RaiseDomainEvents.Should().BeFalse();
					certification.PersonnelCertificationId = 80 + saved.Count;
					saved.Add(certification);
					return certification;
				});
			var credits = new List<PersonnelCertificationCredit>();
			_certifications.Setup(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), Officer, It.IsAny<CancellationToken>()))
				.Callback((PersonnelCertificationCredit credit, string userId, CancellationToken token) => credits.Add(credit))
				.ReturnsAsync((PersonnelCertificationCredit credit, string userId, CancellationToken token) => credit);

			var result = await _service.ImportAsync(DepartmentId, File(
				"Email,Course,Date,Hours,Certification,Certification Number,Issued On,Expires On",
				"jane@example.com,EMT refresher,2025-03-14,3,EMT,E-556677,2024-06-01,2099-06-01",
				"jane@example.com,Airway lab,2025-04-02,2,EMT,E-556677,2024-06-01,2099-06-01",
				"jane@example.com,Old course,2023-01-02,2,EMT,E-556677,2024-06-01,2099-06-01"), null, Officer);

			saved.Should().ContainSingle();
			var created = saved[0];
			created.UserId.Should().Be("u-jane");
			created.DepartmentId.Should().Be(DepartmentId);
			created.DepartmentCertificationTypeId.Should().Be(7);
			created.Type.Should().Be("EMT-Basic");
			created.Name.Should().Be("EMT-Basic");
			created.Number.Should().Be("E-556677");
			created.RecievedOn.Should().Be(new DateTime(2024, 6, 1));
			created.ExpiresOn.Should().Be(new DateTime(2099, 6, 1));
			created.Status.Should().Be((int)PersonnelCertificationStatuses.Active);

			result.CertificationsCreated.Should().Be(1);
			// The 2023 course predates the record's cycle: it stays on the transcript only.
			result.CreditsCreated.Should().Be(2);
			credits.Select(c => c.PersonnelCertificationId).Should().Equal(80, 80);
			credits.Select(c => c.LearningRecordId).Should().Equal(501, 502);
			result.RecordsWritten.Should().Be(3);
		}

		[Test]
		public async Task An_already_expired_certification_is_created_expired()
		{
			MemberHasCertifications();
			PersonnelCertification created = null;
			_certifications.Setup(c => c.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertification certification, ImportEffectPolicy effects, CancellationToken token) =>
				{
					certification.PersonnelCertificationId = 90;
					created = certification;
					return certification;
				});

			var result = await _service.ImportAsync(DepartmentId, File(
				"Email,Course,Date,Hours,Certification,Issued On,Expires On",
				"bob@example.com,EMT refresher,2021-03-14,3,EMT,2020-01-01,2022-01-01"), null, Officer);

			created.Should().NotBeNull();
			created.UserId.Should().Be("u-bob");
			created.Status.Should().Be((int)PersonnelCertificationStatuses.Expired);
			created.StatusReason.Should().Be("imported-expired");
			result.CreditsCreated.Should().Be(1);
		}

		#endregion Import
	}
}
