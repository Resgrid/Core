using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Verification ladder building blocks (wizard plan sections 8.3, 8.4 and 9): typed conversion (L3), the transform
	/// allowlist, header matching and destination detection (L2), excluded-data detectors (section 8.2) and the state rules.
	/// </summary>
	[TestFixture]
	public class ImportLadderTests
	{
		private static ImportFieldContract Field(string type, int? max = null, decimal? min = null, decimal? maxValue = null) =>
			new ImportFieldContract { Key = "f", Type = type, MaxLength = max, Min = min, Max = maxValue };

		private static object Value(ImportFieldContract field, string raw, ImportConversionContext context = null)
		{
			var result = ImportFieldConverter.Convert(field, raw, context ?? new ImportConversionContext());
			result.IssueCode.Should().BeNull();
			return result.Value;
		}

		private static string Code(ImportFieldContract field, string raw, ImportConversionContext context = null) =>
			ImportFieldConverter.Convert(field, raw, context ?? new ImportConversionContext()).IssueCode;

		[Test]
		public void Text_is_trimmed_and_length_checked()
		{
			Value(Field(ImportFieldTypes.Text, 10), "  Station 1 ").Should().Be("Station 1");
			Code(Field(ImportFieldTypes.Text, 3), "Station").Should().Be("IMP-TYPE-TOO-LONG");
			ImportFieldConverter.Convert(Field(ImportFieldTypes.Text), "   ", new ImportConversionContext()).Value.Should().BeNull();
		}

		[Test]
		public void Numbers_are_invariant_and_range_checked()
		{
			Value(Field(ImportFieldTypes.Integer), "12.0").Should().Be("12");
			Value(Field(ImportFieldTypes.Integer), "1 200").Should().Be("1200");
			Code(Field(ImportFieldTypes.Integer), "twelve").Should().Be("IMP-TYPE-INTEGER");
			Code(Field(ImportFieldTypes.Integer, min: 1, maxValue: 600), "601").Should().Be("IMP-TYPE-RANGE");
			Value(Field(ImportFieldTypes.Decimal), "1,5").Should().Be("1.5");
		}

		[TestCase("yes", "true")]
		[TestCase("X", "true")]
		[TestCase("Ja", "true")]
		[TestCase("non", "false")]
		[TestCase("0", "false")]
		public void Boolean_tokens_cover_the_supported_languages(string raw, string expected)
		{
			Value(Field(ImportFieldTypes.Boolean), raw).Should().Be(expected);
		}

		[Test]
		public void Dates_are_asked_about_when_the_day_month_order_is_ambiguous()
		{
			var date = Field(ImportFieldTypes.Date);
			Value(date, "2026-04-03").Should().Be("2026-04-03");
			Value(date, "2026-04-03T00:00:00").Should().Be("2026-04-03");
			Value(date, "25/12/2026").Should().Be("2026-12-25", "a day over 12 settles the order");
			Value(date, "12/25/2026").Should().Be("2026-12-25");
			Code(date, "03/04/2026").Should().Be("IMP-TYPE-DATE-AMBIGUOUS");
			Value(date, "03/04/2026", new ImportConversionContext { DateFormat = "dd/MM/yyyy" }).Should().Be("2026-04-03");
			Value(date, "03/04/2026", new ImportConversionContext { DateFormat = "MM/dd/yyyy" }).Should().Be("2026-03-04");
			Value(date, "4 Mar 2026").Should().Be("2026-03-04");
			Code(date, "31/02/2026").Should().Be("IMP-TYPE-DATE");
		}

		[Test]
		public void Emails_are_lower_cased_and_validated()
		{
			Value(Field(ImportFieldTypes.Email), " Alex.Rivera@Example.ORG ").Should().Be("alex.rivera@example.org");
			Value(Field(ImportFieldTypes.Email), "mailto:a@b.co").Should().Be("a@b.co");
			Code(Field(ImportFieldTypes.Email), "alex@example").Should().Be("IMP-EMAIL-INVALID");
			Code(Field(ImportFieldTypes.Email), "a..b@example.org").Should().Be("IMP-EMAIL-INVALID");
		}

		[Test]
		public void Phones_go_through_the_department_region()
		{
			var phones = new Mock<IPhoneNumberProcesserProvider>();
			phones.Setup(p => p.Process("555 123 4567", "us")).Returns(new PhoneNumberResult { IsValid = true, InternationalNumber = "+15551234567" });
			phones.Setup(p => p.Process("nope", "us")).Returns(new PhoneNumberResult { IsValid = false });
			var context = new ImportConversionContext { Phones = phones.Object, PhoneRegion = "us" };

			Value(Field(ImportFieldTypes.Phone), "555 123 4567", context).Should().Be("+15551234567");
			Code(Field(ImportFieldTypes.Phone), "nope", context).Should().Be("IMP-PHONE-UNPARSEABLE");
		}

		[Test]
		public void Choices_match_codes_aliases_and_administrator_value_maps()
		{
			var field = new ImportFieldContract
			{
				Key = "type",
				Type = ImportFieldTypes.Choice,
				Choices = new List<ImportChoiceCode>
				{
					new ImportChoiceCode { Code = "station", Aliases = new List<string> { "Fire House" } },
					new ImportChoiceCode { Code = "organizational" }
				}
			};

			Value(field, "STATION").Should().Be("station");
			Value(field, "fire-house").Should().Be("station");
			Code(field, "Stn").Should().Be("IMP-CHOICE-UNMAPPED");
			Value(field, "Stn", new ImportConversionContext { ChoiceMap = new Dictionary<string, string> { ["stn"] = "station" } }).Should().Be("station");
		}

		[Test]
		public void Coordinates_colours_time_zones_and_languages_are_typed()
		{
			Value(Field(ImportFieldTypes.Latitude), "39.78170").Should().Be("39.7817");
			Code(Field(ImportFieldTypes.Latitude), "91").Should().Be("IMP-TYPE-COORDINATE");
			Value(Field(ImportFieldTypes.Colour), "2A78D6").Should().Be("#2a78d6");
			Value(Field(ImportFieldTypes.Colour), "#abc").Should().Be("#aabbcc");
			Code(Field(ImportFieldTypes.Colour), "blue").Should().Be("IMP-TYPE-COLOUR");
			Value(Field(ImportFieldTypes.TimeZone), "Pacific Standard Time").Should().Be("Pacific Standard Time");
			Value(Field(ImportFieldTypes.Language), "es-MX").Should().Be("es");
			Code(Field(ImportFieldTypes.Language), "xx").Should().Be("IMP-TYPE-LANGUAGE");
		}

		[Test]
		public void Multi_values_split_on_the_separator_and_fall_back_to_commas()
		{
			var multi = new ImportFieldContract { Key = "roles", Type = ImportFieldTypes.Multi, ItemType = ImportFieldTypes.Text, Separator = ";" };
			Value(multi, "Engineer; EMT;Engineer").Should().BeEquivalentTo(new List<string> { "Engineer", "EMT" });
			Value(multi, "Engineer, EMT").Should().BeEquivalentTo(new List<string> { "Engineer", "EMT" });
		}

		[Test]
		public void Transforms_are_an_allowlist()
		{
			ImportTransforms.Apply(new[] { "name-first" }, "Rivera, Alex").Should().Be("Alex");
			ImportTransforms.Apply(new[] { "name-last" }, "Alex Rivera").Should().Be("Rivera");
			ImportTransforms.Apply(new[] { "upper", "digits-only" }, "ab12").Should().Be("12");
			ImportTransforms.Apply(new[] { "drop table" }, "keep").Should().Be("keep");
			ImportTransforms.IsKnown("eval").Should().BeFalse();
		}

		[Test]
		public void Excluded_data_is_detected_by_header_and_by_value_shape()
		{
			ImportColumnMatcher.IsExcludedHeader("Password").Should().BeTrue();
			ImportColumnMatcher.IsExcludedHeader("Date of Birth").Should().BeTrue();
			ImportColumnMatcher.IsExcludedHeader("SSN").Should().BeTrue();
			ImportColumnMatcher.IsExcludedHeader("Knox Box Code").Should().BeTrue();
			ImportColumnMatcher.IsExcludedHeader("Station").Should().BeFalse();
			ImportColumnMatcher.IsExcludedHeader("Trace").Should().BeFalse();

			ImportColumnMatcher.LooksLikeExcludedValues(new[] { "4111 1111 1111 1111", "5500-0000-0000-0004", "340000000000009" }).Should().BeTrue();
			ImportColumnMatcher.LooksLikeExcludedValues(new[] { "123-45-6789", "987-65-4321", "111-22-3333" }).Should().BeTrue();
			ImportColumnMatcher.LooksLikeExcludedValues(new[] { "555 123 4567", "555 765 4321", "555 000 1111" }).Should().BeFalse();
		}

		[Test]
		public void Resgrid_templates_are_recognized_by_their_field_keys_and_marker()
		{
			var headers = new[] { "First name [first_name]", "Last name [last_name]", "Email [email]", ImportTemplateMarkers.ExampleColumn };
			var detection = ImportColumnMatcher.Detect(headers, "Personas", ImportCatalog.Current);

			detection.EntityKey.Should().Be("personnel");
			detection.AutoConfirm.Should().BeTrue();
			detection.ProfileKey.Should().Be(ImportTemplateMarkers.TemplateProfileKey);
			detection.Map.Columns.Select(c => c.FieldKey).Should().Equal("first_name", "last_name", "email", null);
		}

		[Test]
		public void A_generic_roster_is_proposed_from_header_aliases()
		{
			var headers = new[] { "Member ID", "First", "Surname", "E-mail Address", "Cell Phone", "Home Station", "Shoe size" };
			var detection = ImportColumnMatcher.Detect(headers, "Roster", ImportCatalog.Current);

			detection.EntityKey.Should().Be("personnel");
			detection.AutoConfirm.Should().BeFalse("a generic file is confirmed by the administrator");
			var byHeader = detection.Map.Columns.ToDictionary(c => c.SourceColumn, c => c.FieldKey);
			byHeader["First"].Should().Be("first_name");
			byHeader["Surname"].Should().Be("last_name");
			byHeader["E-mail Address"].Should().Be("email");
			byHeader["Cell Phone"].Should().Be("mobile_phone");
			byHeader["Home Station"].Should().Be("group");
			byHeader["Shoe size"].Should().BeNull();
		}

		[Test]
		public void Workspace_status_is_derived_from_datasets_and_runs()
		{
			var workspace = new ImportWorkspace { Status = (int)ImportWorkspaceStatus.Draft };
			DepartmentImportStateMachine.Derive(workspace, new List<ImportDataset>(), new List<ImportRun>()).Should().Be(ImportWorkspaceStatus.Draft);

			var datasets = new List<ImportDataset> { new ImportDataset { Status = (int)ImportDatasetStatus.NeedsAttention } };
			DepartmentImportStateMachine.Derive(workspace, datasets, new List<ImportRun>()).Should().Be(ImportWorkspaceStatus.Reviewing);

			var commit = new List<ImportRun> { new ImportRun { Kind = (int)ImportRunKind.Commit, Status = (int)ImportRunStatus.Running } };
			DepartmentImportStateMachine.Derive(workspace, datasets, commit).Should().Be(ImportWorkspaceStatus.Importing);

			datasets[0].Status = (int)ImportDatasetStatus.Verified;
			DepartmentImportStateMachine.Derive(workspace, datasets, new List<ImportRun>()).Should().Be(ImportWorkspaceStatus.ReadyToActivate);

			workspace.Status = (int)ImportWorkspaceStatus.Cancelled;
			DepartmentImportStateMachine.Derive(workspace, datasets, commit).Should().Be(ImportWorkspaceStatus.Cancelled, "cancelled is the one hand-set state");
		}

		[Test]
		public void Run_backoff_follows_the_outbox_schedule()
		{
			DepartmentImportStateMachine.Backoff(1).TotalMinutes.Should().Be(1);
			DepartmentImportStateMachine.Backoff(2).TotalMinutes.Should().Be(4);
			DepartmentImportStateMachine.Backoff(3).TotalMinutes.Should().Be(16);
			DepartmentImportStateMachine.Backoff(10).TotalHours.Should().Be(6);
		}

		[Test]
		public void Issue_identity_is_stable_and_value_free()
		{
			var first = ImportIssueFactory.IdentityKey("d1", 5, "Email", "IMP-EMAIL-INVALID");
			first.Should().Be(ImportIssueFactory.IdentityKey("d1", 5, "Email", "IMP-EMAIL-INVALID"));
			first.Should().NotBe(ImportIssueFactory.IdentityKey("d1", 6, "Email", "IMP-EMAIL-INVALID"));
			first.Should().HaveLength(64);
		}
	}
}
