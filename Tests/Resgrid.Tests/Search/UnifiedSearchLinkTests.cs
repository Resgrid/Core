using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Units and groups have no read-only detail page: every viewer gets the page they may open (the unit's events, the group's
	/// row on the groups list) and a caller who passes the edit action's own checks gets the edit page. The link is built from
	/// the entity id, so rows projected when these hits opened the list pages are corrected without a rebuild.
	/// </summary>
	public partial class UnifiedSearchServiceTests
	{
		[TestCase(false, false, "/User/Units/ViewEvents?unitId=9")]
		[TestCase(true, false, "/User/Units/ViewEvents?unitId=9")]
		[TestCase(false, true, "/User/Units/ViewEvents?unitId=9")]
		[TestCase(true, true, "/User/Units/EditUnit?unitId=9")]
		public async Task Unit_hits_open_the_edit_page_only_for_callers_who_may_edit_the_unit(bool updateClaim, bool canModify, string url)
		{
			Answer(Hit(SearchEntityTypes.Unit, "9"));
			_rows[0].Url = "/User/Units"; // projected before hits linked to the item
			_units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { UnitId = 9, DepartmentId = 7 });
			_auth.Setup(a => a.CanUserModifyUnitAsync("u1", 9)).ReturnsAsync(canModify);
			var principal = updateClaim ? Principal("Unit:View", "Unit:Update") : Principal("Unit:View");

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "engine" }, principal);

			result.Hits.Single().Url.Should().Be(url);
		}

		[TestCase(false, false, "/User/Groups#group-9")]
		[TestCase(true, false, "/User/Groups#group-9")]
		[TestCase(false, true, "/User/Groups#group-9")]
		[TestCase(true, true, "/User/Groups/EditGroup?departmentGroupId=9")]
		public async Task Group_hits_open_the_edit_page_only_for_callers_who_may_edit_the_group(bool updateClaim, bool canEdit, string url)
		{
			Answer(Hit(SearchEntityTypes.Group, "9"));
			_rows[0].Url = "/User/Groups"; // projected before hits linked to the item
			_groups.Setup(g => g.GetGroupByIdAsync(9, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 7 });
			_auth.Setup(a => a.CanUserEditDepartmentGroupAsync("u1", 9)).ReturnsAsync(canEdit);
			var principal = updateClaim ? Principal("GenericGroup:View", "GenericGroup:Update") : Principal("GenericGroup:View");

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "station" }, principal);

			result.Hits.Single().Url.Should().Be(url);
		}

		[Test]
		public async Task A_failed_edit_check_keeps_the_read_only_link()
		{
			Answer(Hit(SearchEntityTypes.Unit, "9"));
			_rows[0].Url = "/User/Units"; // projected before hits linked to the item
			_units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { UnitId = 9, DepartmentId = 7 });
			_auth.Setup(a => a.CanUserModifyUnitAsync("u1", 9)).ThrowsAsync(new System.InvalidOperationException("boom"));

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "engine" }, Principal("Unit:View", "Unit:Update"));

			result.Hits.Single().Url.Should().Be("/User/Units/ViewEvents?unitId=9");
		}
	}
}
