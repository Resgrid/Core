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
	/// Round 9C, region neutrality for Canada and the EU: the department's training currency (never a fixed US dollar), amounts in the reader's
	/// culture with the ISO code, and the training hours / CE / CPD file for any reporting body — ISO dates, invariant decimal hours, headings
	/// in the language of the person building it, columns the department chooses — while the US files keep their mandated English layouts.
	/// </summary>
	[TestFixture]
	public class LmsRegionNeutralityTests
	{
		private const int Department = 602;
		private const string Officer = "c0ffee00-0000-4000-8000-000000000011";
		private const string Learner = "c0ffee00-0000-4000-8000-000000000012";
		private static readonly DateTime March = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

		#region Training currency

		[Test]
		public void A_currency_is_an_iso_code_and_the_first_valid_candidate_wins()
		{
			LmsTrainingCurrency.Normalize(" cad ").Should().Be("CAD");
			LmsTrainingCurrency.Normalize("$").Should().BeNull("a symbol could be any dollar");
			LmsTrainingCurrency.Normalize("EURO").Should().BeNull();
			LmsTrainingCurrency.Normalize("e1r").Should().BeNull();
			LmsTrainingCurrency.Resolve(null, "", "gbp", "EUR").Should().Be("GBP");
			LmsTrainingCurrency.Resolve(null, "x").Should().Be(LmsTrainingCurrency.SystemDefault, "nothing valid falls back to the central default only");
			LmsTrainingCurrency.SystemDefault.Should().BeOneOf("USD", "EUR");
		}

		[Test]
		public void Settings_keep_the_departments_currency_and_amounts_follow_the_readers_culture()
		{
			new LmsProgramSettings { RequestCurrency = "chf" }.Normalized().RequestCurrency.Should().Be("CHF");
			new LmsProgramSettings().Normalized().RequestCurrency.Should().Be(LmsTrainingCurrency.SystemDefault);

			LmsTrainingCurrency.Format(1250.5m, "eur", CultureInfo.GetCultureInfo("de")).Should().Be("1.250,50 EUR");
			LmsTrainingCurrency.Format(1250.5m, "CAD", CultureInfo.GetCultureInfo("en")).Should().Be("1,250.50 CAD");
			LmsTrainingCurrency.Format(7m, null, CultureInfo.GetCultureInfo("fr")).Should().Be(7m.ToString("N2", CultureInfo.GetCultureInfo("fr")), "no code, no symbol guessed");
		}

		[Test]
		public async Task The_departments_currency_is_read_from_the_program_settings()
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetSettingByTypeAsync(Department, DepartmentSettingTypes.LmsProgramSettings))
				.ReturnsAsync(new DepartmentSetting { DepartmentId = Department, Setting = ObjectSerialization.Serialize(new LmsProgramSettings { RequestCurrency = "sek" }) });
			(await LmsProgramSettingsService.TrainingCurrencyAsync(settings.Object, Department)).Should().Be("SEK");
			(await LmsProgramSettingsService.TrainingCurrencyAsync(settings.Object, 999)).Should().Be(LmsTrainingCurrency.SystemDefault, "a department that chose none");

			var broken = new Mock<IDepartmentSettingsService>();
			broken.Setup(s => s.GetSettingByTypeAsync(It.IsAny<int>(), It.IsAny<DepartmentSettingTypes>())).ThrowsAsync(new InvalidOperationException("db"));
			(await LmsProgramSettingsService.TrainingCurrencyAsync(broken.Object, Department)).Should().Be(LmsTrainingCurrency.SystemDefault, "a failed read never stops a class being costed");
		}

		#endregion Training currency

		#region Training hours / CE / CPD file

		private static Dictionary<string, LmsExportMember> Members() => new Dictionary<string, LmsExportMember>(StringComparer.OrdinalIgnoreCase)
		{
			[Learner] = new LmsExportMember { UserId = Learner, FirstName = "Élise", LastName = "Tremblay", Email = "elise@example.ca", IdentificationNumber = "QC-7", IsCurrent = true, IsActive = true }
		};

		private static List<LearningRecord> Records() => new List<LearningRecord>
		{
			new LearningRecord
			{
				LearningRecordId = 1, DepartmentId = Department, UserId = Learner, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission, Status = (int)LearningRecordStatuses.Verified,
				Title = "Workplace hazardous materials refresher", CompletedOn = March.AddDays(4).AddHours(15), Hours = 2.5m, CeHours = 2.25m, CategoryId = 4, CourseCode = "WHM-1",
				CeProvider = "Provincial safety association", VerificationCode = "ABCD-1234"
			},
			new LearningRecord
			{
				LearningRecordId = 2, DepartmentId = Department, UserId = Learner, SourceKind = (int)LearningRecordSourceKinds.ClassSession, Status = (int)LearningRecordStatuses.Pending,
				Title = "Not verified", CompletedOn = March.AddDays(6), Hours = 1
			}
		};

		private static Dictionary<int, TrainingCategory> Categories() => new Dictionary<int, TrainingCategory> { [4] = new TrainingCategory { TrainingCategoryId = 4, Name = "Workplace safety", ExternalCode = "WS" } };

		[Test]
		public void The_training_hours_file_is_first_needs_no_us_identifiers_and_localizes_its_default_headings()
		{
			var exporter = LmsCeExporterCatalog.Find(LmsCeExporterCatalog.TrainingHours);
			exporter.Should().NotBeNull();
			LmsCeExporterCatalog.All.First().Key.Should().Be(LmsCeExporterCatalog.TrainingHours, "the file any department can use leads the list");
			exporter.Kind.Should().Be(LmsCeExporterKinds.TrainingHours);
			exporter.StateCode.Should().BeNull();
			exporter.RequiresCeNumber.Should().BeFalse();
			exporter.OwnProviderOnly.Should().BeFalse();
			exporter.LocalizedHeaders.Should().BeTrue();
			exporter.DefaultColumns.Select(c => (LmsCeColumnSources)c.Source).Should().NotContain(new[]
			{
				LmsCeColumnSources.LicenseState, LmsCeColumnSources.NremtNumber, LmsCeColumnSources.NremtExpiresOn, LmsCeColumnSources.Nemsid,
				LmsCeColumnSources.CapceProviderNumber, LmsCeColumnSources.CeBrokerProviderNumber
			});
			exporter.DefaultColumns.Should().OnlyContain(c => c.HeaderKey == "Ce.Source." + (LmsCeColumnSources)c.Source);
			exporter.DefaultColumns.Where(c => c.Source == (int)LmsCeColumnSources.CompletedOn).Should().OnlyContain(c => c.Argument == "yyyy-MM-dd");

			// The US layouts keep their mandated English headings whatever the reader's language.
			LmsCeExporterCatalog.All.Where(e => e.Key != LmsCeExporterCatalog.TrainingHours).Should().OnlyContain(e => !e.LocalizedHeaders);
			var capce = new LmsCeReportingSettings().ColumnsFor(LmsCeExporterCatalog.Find(LmsCeExporterCatalog.Capce));
			LmsCeExportRules.LocalizeHeaders(capce, key => "translated").Select(c => c.Header).Should().Contain("Date of Completion");
		}

		[Test]
		public void The_training_hours_file_has_iso_dates_invariant_hours_and_department_local_days()
		{
			var exporter = LmsCeExporterCatalog.Find(LmsCeExporterCatalog.TrainingHours);
			var settings = new LmsCeReportingSettings().Normalized();
			var columns = LmsCeExportRules.LocalizeHeaders(settings.ColumnsFor(exporter), key => LmsExportText.Get(key, CultureInfo.GetCultureInfo("fr")));
			var selected = LmsCeExportRules.Select(exporter, Records(), settings, March, March.AddMonths(1), Members(), false, null, Department);
			// Toronto in March 2026 (before the daylight-saving change): UTC-5.
			var file = LmsCeExportRules.Build(exporter, columns, selected, Members(), new Dictionary<string, LmsCeMemberIdentity>(), settings, Categories(), utc => utc.AddHours(-5));
			file.Bytes = LmsCeExportRules.ToBytes(file);

			file.Rows.Should().ContainSingle("only verified completions; no CE number or provider is needed");
			var row = file.Rows[0];
			var cell = new Func<LmsCeColumnSources, string>(source => row[columns.FindIndex(c => c.Source == (int)source)]);
			cell(LmsCeColumnSources.CompletedOn).Should().Be("2026-03-05");
			cell(LmsCeColumnSources.Hours).Should().Be("2.5", "a full stop, whatever the reader's language");
			cell(LmsCeColumnSources.CeHours).Should().Be("2.25");
			cell(LmsCeColumnSources.LastName).Should().Be("Tremblay");
			cell(LmsCeColumnSources.CategoryCode).Should().Be("WS");
			cell(LmsCeColumnSources.VerificationCode).Should().Be("ABCD-1234");
			file.MissingValues.Should().BeEmpty("no column of this file names a US identifier");

			file.Headers.Should().Contain(LmsExportText.Get("Ce.Source.CompletedOn", CultureInfo.GetCultureInfo("fr")));
			file.Headers.Should().NotContain("Completion date", "the headings are French for a French reader");
			var text = Encoding.UTF8.GetString(file.Bytes);
			text.Should().Contain("Tremblay").And.Contain("2026-03-05");
		}

		[Test]
		public void Default_headings_follow_each_language_and_a_departments_own_columns_keep_what_it_typed()
		{
			var exporter = LmsCeExporterCatalog.Find(LmsCeExporterCatalog.TrainingHours);
			foreach (var language in new[] { "en", "de", "es", "fr", "it", "pl", "sv", "uk", "el", "ar" })
			{
				var culture = CultureInfo.GetCultureInfo(language);
				var headers = LmsCeExportRules.LocalizeHeaders(new LmsCeReportingSettings().ColumnsFor(exporter), key => LmsExportText.Get(key, culture)).Select(c => c.Header).ToList();
				headers.Should().OnlyHaveUniqueItems(language);
				headers.Should().NotContain(h => h.StartsWith("Ce.Source.", StringComparison.Ordinal), "every heading has a {0} translation", language);
				if (language != "en")
					headers.Should().NotContain("Completion date", language);
			}

			var custom = new LmsCeReportingSettings
			{
				ColumnMaps = new List<LmsCeColumnMap>
				{
					new LmsCeColumnMap { ExporterKey = LmsCeExporterCatalog.TrainingHours, Columns = { new LmsCeColumn("Nom", LmsCeColumnSources.LastName), new LmsCeColumn("Heures", LmsCeColumnSources.Hours, "0.0") } }
				}
			}.Normalized();
			var own = LmsCeExportRules.LocalizeHeaders(custom.ColumnsFor(exporter), key => LmsExportText.Get(key, CultureInfo.GetCultureInfo("de")));
			own.Select(c => c.Header).Should().Equal("Nom", "Heures");
			own.Should().OnlyContain(c => c.HeaderKey == null, "a stored column list never carries a heading key");
		}

		[Test]
		public async Task The_service_writes_the_headings_in_the_builders_language()
		{
			var settings = new Mock<IDepartmentSettingsService>();
			var records = new Mock<ILearningRecordService>();
			records.Setup(r => r.QueryAsync(It.IsAny<LearningRecordQuery>()))
				.ReturnsAsync((LearningRecordQuery q) => q.Skip == 0 ? Records() : new List<LearningRecord>());
			records.Setup(r => r.GetCategoriesAsync(Department, true)).ReturnsAsync(Categories().Values.ToList());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Department, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Department, TimeZone = "Eastern Standard Time" });
			departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(Department)).ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = Department, UserId = Learner } });
			departments.Setup(d => d.GetAllUsersForDepartmentUnlimitedAsync(Department, It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser>());
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetAllProfilesForDepartmentIncDisabledDeletedAsync(Department))
				.ReturnsAsync(new Dictionary<string, UserProfile> { [Learner] = new UserProfile { UserId = Learner, FirstName = "Élise", LastName = "Tremblay" } });
			var service = new LmsCeExportService(settings.Object, records.Object, Mock.Of<ICertificationService>(), departments.Object, profiles.Object, Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IProtectedReadService>(), Mock.Of<IProtectedGrantContext>());
			var actor = new LmsActor
			{
				DepartmentId = Department, UserId = Officer, IsCurrentMember = true, IsDepartmentAdmin = true,
				Gate = new LmsDepartmentGate { DepartmentId = Department, ModuleOn = true, FlagOn = true, AddonActive = true }
			};

			Task<LmsCeExportFile> Build(string language) => service.BuildAsync(actor, false, new LmsCeExportRequest
			{
				ExporterKey = LmsCeExporterCatalog.TrainingHours, FromLocal = new DateTime(2026, 3, 1), ToLocal = new DateTime(2026, 3, 31),
				Culture = language == null ? null : CultureInfo.GetCultureInfo(language)
			}, false, CancellationToken.None);

			var german = await Build("de");
			german.Problem.Should().BeNull();
			german.FileName.Should().Be("training-hours-20260301-20260331.csv");
			german.Headers.Should().Contain(LmsExportText.Get("Ce.Source.CompletedOn", CultureInfo.GetCultureInfo("de")));
			german.Rows.Should().ContainSingle().Which.Should().Contain("2026-03-05");
			(await Build(null)).Headers.Should().Contain("Completion date", "English when the builder's language is unknown");
		}

		#endregion Training hours / CE / CPD file
	}
}
