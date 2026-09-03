using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Migration 141's contract work is intentionally deferred. During the expand/relocate window,
	/// every application write for the fields it will eventually remove must land on
	/// DepartmentMemberSensitiveData, while the UserProfiles values remain read-only migration
	/// sources. These structural checks cover both forms that accept an identification number and
	/// the only form that accepts the moved addresses.
	/// </summary>
	[TestFixture]
	public class MemberProfileMigrationWritePathTests
	{
		private static DirectoryInfo RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the tests must be able to find the repository root");
			return directory!;
		}

		private static string ControllerSource(string controller)
		{
			var path = Path.Combine(RepositoryRoot().FullName, "Web", "Resgrid.Web", "Areas", "User",
				"Controllers", controller);
			File.Exists(path).Should().BeTrue($"expected the controller at {path}");
			return File.ReadAllText(path);
		}

		private static string MethodBody(string source, string signature)
		{
			var start = source.IndexOf(signature, StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, $"expected method signature {signature}");

			var next = Regex.Match(source.Substring(start + 1),
				@"(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?Task<[^>]+>\s+\w+\s*\(");

			return next.Success ? source.Substring(start, next.Index + 1) : source.Substring(start);
		}

		[Test]
		public void Add_person_writes_moved_fields_to_the_department_row_only()
		{
			var body = MethodBody(ControllerSource("PersonnelController.cs"),
				"public async Task<IActionResult> AddPerson(AddPersonModel model");

			var clearLegacy = body.IndexOf("model.Profile.IdentificationNumber = null;", StringComparison.Ordinal);
			var saveProfile = body.IndexOf("_userProfileService.SaveProfileAsync", StringComparison.Ordinal);
			var addMembership = body.IndexOf("_departmentsService.AddUserToDepartmentAsync", StringComparison.Ordinal);
			var saveDepartmentValue = body.IndexOf("_memberSensitiveDataService.SaveAsync", StringComparison.Ordinal);

			clearLegacy.Should().BeGreaterThan(0, "the posted value must not reach the global profile writer");
			clearLegacy.Should().BeLessThan(saveProfile);
			body.Should().Contain("model.Profile.HomeAddressId = null;",
				"an overposted legacy home-address link must not reach a new global profile row");
			body.Should().Contain("model.Profile.MailingAddressId = null;",
				"an overposted legacy mailing-address link must not reach a new global profile row");
			saveDepartmentValue.Should().BeGreaterThan(addMembership,
				"the department-scoped row should be created only after its membership exists");
			body.Should().Contain("IdentificationNumber = identificationNumber");
			body.Should().Contain("LegacyProfileRelocatedOn = DateTime.UtcNow",
				"a brand-new profile has no legacy source for the relocation worker to revisit");
			body.Should().Contain("catch (InvalidOperationException ex)",
				"a protected-write failure must not abandon the remaining user-creation steps");
			body.Should().Contain("Logging.LogException(ex);");
			body.Should().Contain("}, cancellationToken);",
				"cancellation must still be passed to the department-scoped save");
		}

		[Test]
		public void Edit_profile_writes_all_moved_fields_to_one_department_row()
		{
			var source = ControllerSource("HomeController.cs");
			var post = MethodBody(source,
				"public async Task<IActionResult> EditUserProfile(EditProfileModel model");
			var writer = MethodBody(source,
				"private async Task SaveMemberSensitiveProfileAsync(EditProfileModel model");

			post.Should().Contain("SaveMemberSensitiveProfileAsync(model, savedProfile, cancellationToken)");
			post.Should().NotContain("savedProfile.IdentificationNumber = model.Profile.IdentificationNumber",
				"the global profile column is a read-only migration source");

			foreach (var assignment in new[]
			{
				"sensitive.IdentificationNumber = v",
				"sensitive.HomeAddress1 = v", "sensitive.HomeCity = v", "sensitive.HomeState = v",
				"sensitive.HomePostalCode = v", "sensitive.HomeCountry = v",
				"sensitive.MailingAddress1 = v", "sensitive.MailingCity = v", "sensitive.MailingState = v",
				"sensitive.MailingPostalCode = v", "sensitive.MailingCountry = v"
			})
			{
				writer.Should().Contain(assignment);
			}

			writer.Should().Contain("value == ProtectedDataEnvelope.RedactionValue",
				"an unrevealed protected value must never be overwritten by its UI sentinel");
			writer.Should().Contain("sensitive.LegacyProfileRelocatedOn = DateTime.UtcNow",
				"an unprotected edit of the complete form must win over stale legacy values");
		}

		[Test]
		public void Pre_contract_fallback_is_read_only_guarded_and_stops_after_relocation()
		{
			var source = ControllerSource("HomeController.cs");
			var get = MethodBody(source,
				"public async Task<IActionResult> EditUserProfile(string userId)");

			get.Should().Contain("!protectionEnforced");
			get.Should().Contain("!memberAddresses.LegacyProfileRelocatedOn.HasValue",
				"a blank target after the marker can be an intentional clear and must not fall back");
			get.Should().Contain("model.Profile.HomeAddressId.Value");
			get.Should().Contain("model.Profile.MailingAddressId.Value");
			get.IndexOf("await HydrateMemberIdentificationNumberAsync", StringComparison.Ordinal).Should().BeGreaterThan(
				get.IndexOf("model.Profile = new UserProfile();", StringComparison.Ordinal),
				"the fallback profile must exist before department-scoped identification data is hydrated");
			get.Should().NotContain("savedProfile.HomeAddressId =",
				"legacy addresses are displayed only to bridge the relocation window, never rewritten");
		}
	}
}
