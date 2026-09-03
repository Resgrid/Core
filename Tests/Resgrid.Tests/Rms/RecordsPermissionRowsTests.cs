using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Security;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The Records block on the Permissions admin screen is generated from RecordPermissionCatalog
	/// (registry section 4.4). These tests pin that the screen preselects exactly the no-row default the
	/// claim chain evaluates, offers value 4 everywhere, withholds "Everyone" on admin-anchored rows, and
	/// that every generated resource key exists in every supported language.
	/// </summary>
	[TestFixture]
	public class RecordsPermissionRowsTests
	{
		private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "pl", "sv", "uk", "el", "ar" };

		private static readonly PermissionTypes[] AdminAnchored =
		{
			PermissionTypes.SubmitRecords,
			PermissionTypes.ShareRecordsExternally,
			PermissionTypes.ViewRestrictedRecords,
			PermissionTypes.ManageRecordDefinitions,
			PermissionTypes.PublishRecordDefinitions,
			PermissionTypes.ManageRecordReports,
			PermissionTypes.ManageRecordDisclosures,
			PermissionTypes.ManageRecordLegalHold
		};

		[Test]
		public void Build_WithNoRows_RendersEveryCatalogEntryAtItsNoRowDefault()
		{
			var rows = RecordsPermissionRows.Build(new List<Permission>());

			rows.Select(r => r.Type).Should().Equal(RecordPermissionCatalog.All.Select(d => d.Type));

			foreach (var row in rows)
			{
				var descriptor = RecordPermissionCatalog.Get(row.Type);

				row.HasRow.Should().BeFalse(row.Name);
				row.Value.Should().Be((int)descriptor.NoRowDefault, row.Name);
				row.LockToGroup.Should().BeFalse(row.Name);
				row.ShowLockToGroup.Should().Be(descriptor.LockToGroupMeaningful, row.Name);

				var options = row.Options.ToList();
				options.Select(o => o.Value).Should().Contain(RecordsPermissionRows.DepartmentAndGroupAdminsAndSelectRolesValue, row.Name);
				options.Any(o => o.Value == RecordsPermissionRows.EveryoneValue).Should().Be(descriptor.EveryoneOffered, row.Name);
				options.Single(o => o.Selected).Value.Should().Be(row.Value.ToString(), row.Name);
			}
		}

		[Test]
		public void Build_WithExistingRow_UsesStoredActionAndLock()
		{
			var rows = RecordsPermissionRows.Build(new[]
			{
				new Permission
				{
					PermissionType = (int)PermissionTypes.ReviewRecords,
					Action = (int)PermissionActions.DepartmentAndGroupAdminsAndSelectRoles,
					LockToGroup = true
				}
			});

			var review = rows.Single(r => r.Type == PermissionTypes.ReviewRecords);
			review.HasRow.Should().BeTrue();
			review.Value.Should().Be(4);
			review.LockToGroup.Should().BeTrue();
			review.Options.Single(o => o.Selected).Value.Should().Be("4");

			rows.Where(r => r.Type != PermissionTypes.ReviewRecords).Should().OnlyContain(r => !r.HasRow);
		}

		[Test]
		public void Build_StoredEveryoneOnAdminAnchoredRow_IsStillListedSoTheDropdownNeverLies()
		{
			var rows = RecordsPermissionRows.Build(new[]
			{
				new Permission { PermissionType = (int)PermissionTypes.ManageRecordLegalHold, Action = (int)PermissionActions.Everyone }
			});

			var row = rows.Single(r => r.Type == PermissionTypes.ManageRecordLegalHold);
			RecordPermissionCatalog.Get(row.Type).EveryoneOffered.Should().BeFalse();
			row.Options.Single(o => o.Selected).Value.Should().Be(RecordsPermissionRows.EveryoneValue);
		}

		[Test]
		public void Catalog_AdminAnchoredPermissions_DoNotOfferEveryone()
		{
			foreach (var descriptor in RecordPermissionCatalog.All)
				descriptor.EveryoneOffered.Should().Be(!AdminAnchored.Contains(descriptor.Type), descriptor.Type.ToString());

			// A no-row default of Everyone must always be offered, or the screen could not show the default.
			RecordPermissionCatalog.All
				.Where(d => d.NoRowDefault == PermissionActions.Everyone)
				.Should().OnlyContain(d => d.EveryoneOffered);
		}

		[Test]
		public void ElementIds_AreUniqueAndDerivableFromThePermissionName()
		{
			var rows = RecordsPermissionRows.Build(null);

			rows.Select(r => r.ElementId).Should().OnlyHaveUniqueItems();
			rows.Should().OnlyContain(r =>
				r.ElementId == "Record_" + r.Type &&
				r.LockElementId == "Lock_Record_" + r.Type &&
				r.LabelKey == "PermRecords" + r.Type + "Label" &&
				r.NoteKey == "PermRecords" + r.Type + "Note");
		}

		[Test]
		public void SecurityResx_CarriesLabelAndNoteForEveryRowInEveryLanguage()
		{
			var root = LocalizationRoot();
			var rows = RecordsPermissionRows.Build(null);
			var sectionKeys = new[] { "PermRecordsSectionHeader", "PermRecordsSectionNote", "PermRecordsNotActivatedNote" };

			foreach (var language in Languages)
			{
				var entries = Load(Path.Combine(root, "Areas", "User", "Security", $"Security.{language}.resx"));

				foreach (var key in sectionKeys)
					entries.Should().ContainKey(key, $"{language} must carry {key}");

				foreach (var row in rows)
				{
					entries.Should().ContainKey(row.LabelKey, $"{language} must carry {row.LabelKey}");
					entries.Should().ContainKey(row.NoteKey, $"{language} must carry {row.NoteKey}");
					entries[row.LabelKey].Should().NotBeNullOrWhiteSpace(row.LabelKey);
					entries[row.NoteKey].Should().NotBeNullOrWhiteSpace(row.NoteKey);
				}
			}
		}

		private static string LocalizationRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Core", "Resgrid.Localization");
		}

		private static Dictionary<string, string> Load(string path)
		{
			return XDocument.Load(path)
				.Root!
				.Elements("data")
				.ToDictionary(
					x => (string)x.Attribute("name")!,
					x => (string)x.Element("value") ?? string.Empty);
		}
	}
}
