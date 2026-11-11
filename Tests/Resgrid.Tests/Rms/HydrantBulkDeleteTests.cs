using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>Bulk hydrant removal: a department that imported the wrong file (thousands of rows) clears it without deleting one at a time.</summary>
	[TestFixture]
	public class HydrantBulkDeleteTests
	{
		private static RmsHydrant Seed(RmsPreventionHarness h, int departmentId, string number, string source, DateTime? deletedOn = null)
		{
			var hydrant = new RmsHydrant { RmsHydrantId = Guid.NewGuid().ToString(), DepartmentId = departmentId, HydrantNumber = number, Latitude = 45, Longitude = -122, InService = true, Source = source, CreatedOn = DateTime.UtcNow.AddDays(-1), ModifiedOn = DateTime.UtcNow.AddDays(-1), RowVersion = 3, DeletedOn = deletedOn };
			h.Hydrants.Rows.Add(hydrant);
			return hydrant;
		}

		[Test]
		public async Task All_removes_every_live_hydrant_of_the_department_and_nothing_else()
		{
			var h = new RmsPreventionHarness();
			var manual = Seed(h, Dept, "M-1", RmsHydrantSources.Manual);
			var imported = Seed(h, Dept, "I-1", "csv-import");
			var alreadyGone = Seed(h, Dept, "OLD", "csv-import", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
			var otherDepartment = Seed(h, Dept + 1, "X-1", "csv-import");

			var removed = await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.All, null, 2);

			removed.Should().Be(2);
			manual.DeletedOn.Should().NotBeNull();
			imported.DeletedOn.Should().NotBeNull();
			manual.RowVersion.Should().Be(4, "a removal is a change other writers must see");
			alreadyGone.DeletedOn.Should().Be(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "an earlier removal keeps its date");
			alreadyGone.RowVersion.Should().Be(3);
			otherDepartment.DeletedOn.Should().BeNull();
			(await h.HydrantsService.ListAsync(Dept, Member)).Should().BeEmpty();
			(await h.HydrantsService.GetMapLayerAsync(Dept, Member, null, null, null, null)).Should().BeEmpty();
			var audit = h.Audits.Rows.Single(a => a.Purpose == "Hydrants removed in bulk");
			audit.DepartmentId.Should().Be(Dept);
			audit.ActorUserId.Should().Be(Admin);
		}

		[Test]
		public async Task Imported_removes_imported_hydrants_and_keeps_manual_ones()
		{
			var h = new RmsPreventionHarness();
			var manual = await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "M-1", Latitude = 45, Longitude = -122 });
			(await h.HydrantsService.ImportCsvAsync(Dept, Admin, "number,latitude,longitude\nI-1,45.1,-122.1\nI-2,45.2,-122.2\nI-3,45.3,-122.3")).Created.Should().Be(3);
			var json = await h.HydrantsService.ImportAsync(Dept, Admin, "[{\"number\":\"J-1\",\"latitude\":45.4,\"longitude\":-122.4}]", "json");
			json.Created.Should().Be(1);

			var removed = await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Imported, null, 4);

			removed.Should().Be(4);
			(await h.HydrantsService.ListAsync(Dept, Admin)).Select(x => x.RmsHydrantId).Should().Equal(manual.RmsHydrantId);
		}

		[Test]
		public async Task Selected_removes_only_the_listed_live_hydrants_of_this_department()
		{
			var h = new RmsPreventionHarness();
			var keep = Seed(h, Dept, "K-1", RmsHydrantSources.Manual);
			var a = Seed(h, Dept, "A-1", "csv-import");
			var b = Seed(h, Dept, "B-1", RmsHydrantSources.Manual);
			var foreign = Seed(h, Dept + 1, "F-1", "csv-import");

			// A foreign id or one that does not exist is ignored, never removed; the confirmation counts only what this department loses.
			var removed = await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Selected, new[] { a.RmsHydrantId, b.RmsHydrantId, b.RmsHydrantId, " ", foreign.RmsHydrantId, "missing" }, 2);

			removed.Should().Be(2);
			a.DeletedOn.Should().NotBeNull();
			b.DeletedOn.Should().NotBeNull();
			keep.DeletedOn.Should().BeNull();
			foreign.DeletedOn.Should().BeNull();
		}

		[Test]
		public async Task Selected_without_a_count_is_allowed_because_the_ids_name_the_rows()
		{
			var h = new RmsPreventionHarness();
			var a = Seed(h, Dept, "A-1", "csv-import");
			(await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Selected, new[] { a.RmsHydrantId }, null)).Should().Be(1);
		}

		[TestCase(HydrantBulkDeleteScope.All)]
		[TestCase(HydrantBulkDeleteScope.Imported)]
		public async Task Department_wide_scopes_need_the_matching_count(HydrantBulkDeleteScope scope)
		{
			var h = new RmsPreventionHarness();
			Seed(h, Dept, "I-1", "csv-import");
			Seed(h, Dept, "I-2", "json-import");

			Func<Task> unconfirmed = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, scope, null, null);
			await unconfirmed.Should().ThrowAsync<ArgumentException>().WithMessage("*Confirm how many*");
			// A stale page (or an import that ran since) confirms the wrong number: nothing is removed.
			Func<Task> stale = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, scope, null, 1);
			await stale.Should().ThrowAsync<ArgumentException>().WithMessage("2 hydrants match*not 1*Reload*");

			h.Hydrants.BulkSoftDeletes.Should().Be(0);
			h.Hydrants.Rows.Should().OnlyContain(x => x.DeletedOn == null);
			h.Audits.Rows.Should().NotContain(a => a.Purpose == "Hydrants removed in bulk");
		}

		[Test]
		public async Task A_hydrant_imported_between_the_count_and_the_removal_rolls_the_removal_back()
		{
			var h = new RmsPreventionHarness();
			Seed(h, Dept, "I-1", "csv-import");
			Seed(h, Dept, "I-2", "csv-import");
			// The administrator confirmed 2; an import commits a third before the UPDATE runs, so the statement matches 3.
			h.Hydrants.BeforeBulkSoftDelete = () => Seed(h, Dept, "I-3", "csv-import");

			Func<Task> raced = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Imported, null, 2);

			await raced.Should().ThrowAsync<ArgumentException>().WithMessage("3 hydrants match*not 2*Reload*");
			h.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once, "the UPDATE is rolled back, not committed");
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			h.Audits.Rows.Should().NotContain(a => a.Purpose == "Hydrants removed in bulk");
		}

		[Test]
		public async Task The_removal_and_its_audit_row_commit_together()
		{
			var h = new RmsPreventionHarness();
			Seed(h, Dept, "I-1", "csv-import");

			(await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.All, null, 1)).Should().Be(1);

			h.UnitOfWork.Verify(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
			h.UnitOfWork.Verify(x => x.CommitChanges(), Times.Once);
			h.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Never);
			h.Audits.Rows.Should().ContainSingle(a => a.Purpose == "Hydrants removed in bulk");
		}

		[Test]
		public async Task Invalid_requests_are_rejected_before_anything_is_removed()
		{
			var h = new RmsPreventionHarness();
			Seed(h, Dept, "I-1", "csv-import");

			Func<Task> noIds = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Selected, new[] { "", null }, null);
			await noIds.Should().ThrowAsync<ArgumentException>().WithMessage("Select the hydrants*");
			Func<Task> tooMany = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Selected, Enumerable.Range(0, RecordsHydrantsService.MaxBulkDeleteSelection + 1).Select(i => "id-" + i), null);
			await tooMany.Should().ThrowAsync<ArgumentException>().WithMessage("*at most*");
			Func<Task> unknownScope = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, (HydrantBulkDeleteScope)9, null, 1);
			await unknownScope.Should().ThrowAsync<ArgumentException>();

			h.Hydrants.BulkSoftDeletes.Should().Be(0);
			h.Hydrants.Rows.Should().OnlyContain(x => x.DeletedOn == null);
		}

		[Test]
		public async Task Nothing_matching_removes_nothing_and_records_nothing()
		{
			var h = new RmsPreventionHarness();
			Seed(h, Dept, "M-1", RmsHydrantSources.Manual);

			(await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.Imported, null, 0)).Should().Be(0);

			h.Hydrants.BulkSoftDeletes.Should().Be(0);
			h.Audits.Rows.Should().NotContain(a => a.Purpose == "Hydrants removed in bulk");
		}

		[Test]
		public async Task Bulk_removal_needs_a_prevention_admin_and_the_hydrants_module()
		{
			var h = new RmsPreventionHarness();
			Seed(h, Dept, "I-1", "csv-import");

			Func<Task> member = () => h.HydrantsService.BulkDeleteAsync(Dept, Member, HydrantBulkDeleteScope.All, null, 1);
			await member.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> outsider = () => h.HydrantsService.BulkDeleteAsync(Dept, Outsider, HydrantBulkDeleteScope.All, null, 1);
			await outsider.Should().ThrowAsync<UnauthorizedAccessException>();
			h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionHydrants);
			Func<Task> disabled = () => h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.All, null, 1);
			await disabled.Should().ThrowAsync<RecordsModuleDisabledException>();

			h.Hydrants.Rows.Should().OnlyContain(x => x.DeletedOn == null);
		}

		[Test]
		public async Task Importing_the_file_again_after_a_removal_creates_fresh_hydrants()
		{
			var h = new RmsPreventionHarness();
			const string csv = "number,latitude,longitude\nH-1,45.1,-122.1\nH-2,45.2,-122.2";
			(await h.HydrantsService.ImportCsvAsync(Dept, Admin, csv)).Created.Should().Be(2);
			(await h.HydrantsService.BulkDeleteAsync(Dept, Admin, HydrantBulkDeleteScope.All, null, 2)).Should().Be(2);

			var again = await h.HydrantsService.ImportCsvAsync(Dept, Admin, csv);

			again.Created.Should().Be(2);
			again.Updated.Should().Be(0);
			(await h.HydrantsService.ListAsync(Dept, Admin)).Should().HaveCount(2).And.OnlyContain(x => x.DeletedOn == null);
		}

		[TestCase("csv-import", true)]
		[TestCase("json-import", true)]
		[TestCase("manual", false)]
		[TestCase(null, false)]
		[TestCase("CSV-IMPORT", false)]
		public void Imported_source_matches_what_the_import_writes(string source, bool imported)
			=> RmsHydrantSources.IsImported(source).Should().Be(imported);
	}
}
