using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Security;
using File = System.IO.File;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Match = System.Text.RegularExpressions.Match;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Security > Permissions screen against the 2026-10-04 permission audit: role and lock writes on rows the
	/// department has not saved yet, the group lock round trip on the call rows, no-row preselects that match the
	/// runtime, and SetPermission / SetPermissionData refusing anything the screen does not offer.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class SecurityPermissionScreenTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "permissions-admin";

		private List<Permission> _rows;
		private Mock<IPermissionsService> _permissions;
		private Mock<IEventAggregator> _events;
		private SecurityController _controller;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<Permission>();
			_permissions = new Mock<IPermissionsService>();
			_permissions.Setup(p => p.GetAllPermissionsForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _rows.ToList());
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(DepartmentId, It.IsAny<PermissionTypes>()))
				.ReturnsAsync((int department, PermissionTypes type) => _rows.FirstOrDefault(r => r.PermissionType == (int)type));
			_permissions.Setup(p => p.SetPermissionForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<PermissionTypes>(), It.IsAny<PermissionActions>(),
					It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int department, string user, PermissionTypes type, PermissionActions action, string data, bool lockToGroup, CancellationToken ct) =>
					new Permission { DepartmentId = department, PermissionType = (int)type, Action = (int)action, Data = data, LockToGroup = lockToGroup });
			_events = new Mock<IEventAggregator>();

			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name, resourceNotFound: true));

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			// The audit event records the caller's address.
			httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new SecurityController(
				departments.Object,
				Mock.Of<IAuditService>(),
				_permissions.Object,
				_events.Object,
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<ISystemAuditsService>(),
				new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null).Object,
				localizer.Object,
				Mock.Of<IDepartmentSsoService>(),
				Mock.Of<IEncryptionService>(),
				Mock.Of<IRecordsCutoverService>(),
				Mock.Of<IPasskeyFeatureGates>(),
				Mock.Of<IMfaEvidenceService>(),
				Mock.Of<IMfaPolicyService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		// ── Defect 1: role and lock writes on rows that were never saved ─────────────────────────────

		[Test]
		public async Task SetPermissionData_creates_every_unsaved_row_at_the_action_and_lock_the_screen_shows()
		{
			foreach (var entry in PermissionScreenCatalog.All)
			{
				_permissions.Invocations.Clear();

				var result = await _controller.SetPermissionData((int)entry.Type, "3", null);

				if (!entry.RolesOffered || entry.NoRowValue == PermissionScreenCatalog.NotSavedValue)
				{
					StatusOf(result).Should().Be(400, entry.Type.ToString());
					VerifyNothingSaved();
					continue;
				}

				StatusOf(result).Should().Be(200, entry.Type.ToString());
				VerifySaved(entry.Type, entry.NoRowValue, "3", entry.LockToGroupOffered && entry.NoRowLockToGroup);
			}
		}

		[TestCase(PermissionTypes.ViewAllWorkOrders, (int)PermissionActions.DepartmentAndGroupAdmins, true)]
		[TestCase(PermissionTypes.ViewChecklistResults, (int)PermissionActions.DepartmentAndGroupAdmins, true)]
		[TestCase(PermissionTypes.ManageChecklists, (int)PermissionActions.DepartmentAdminsOnly, false)]
		[TestCase(PermissionTypes.CreateRecord, (int)PermissionActions.Everyone, false)]
		[TestCase(PermissionTypes.AdjustInventory, (int)PermissionActions.DepartmentAdminsOnly, false)]
		[TestCase(PermissionTypes.CreateCall, (int)PermissionActions.Everyone, false)]
		public async Task SetPermissionData_on_an_unsaved_row_uses_its_runtime_default(PermissionTypes type, int action, bool lockToGroup)
		{
			var result = await _controller.SetPermissionData((int)type, "3,7", null);

			StatusOf(result).Should().Be(200);
			VerifySaved(type, action, "3,7", lockToGroup);
		}

		[TestCase(PermissionTypes.TransferInventory)]
		[TestCase(PermissionTypes.IssueInventory)]
		public async Task SetPermissionData_on_unsaved_transfer_or_issue_follows_the_adjust_inventory_rule(PermissionTypes type)
		{
			Row(PermissionTypes.AdjustInventory, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "5", lockToGroup: true);

			// The page used to re-post the inherited Adjust Inventory roles on load and hit a null row here.
			var result = await _controller.SetPermissionData((int)type, "5", null);

			StatusOf(result).Should().Be(200);
			VerifySaved(type, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "5", true);
		}

		[TestCase(PermissionTypes.TransferInventory)]
		[TestCase(PermissionTypes.IssueInventory)]
		public async Task SetPermissionData_with_neither_inventory_rule_saved_uses_department_admins(PermissionTypes type)
		{
			var result = await _controller.SetPermissionData((int)type, "9", false);

			StatusOf(result).Should().Be(200);
			VerifySaved(type, (int)PermissionActions.DepartmentAdminsOnly, "9", false);
		}

		[TestCase(PermissionTypes.CreateWorkflow)]
		[TestCase(PermissionTypes.ManageWorkflowCredentials)]
		[TestCase(PermissionTypes.ViewWorkflowRuns)]
		public async Task SetPermissionData_on_an_unsaved_workflow_row_is_refused_because_no_action_is_chosen(PermissionTypes type)
		{
			var result = await _controller.SetPermissionData((int)type, "3", null);

			StatusOf(result).Should().Be(400);
			VerifyNothingSaved();
		}

		[TestCase("abc")]
		[TestCase("1;2")]
		[TestCase("-1")]
		[TestCase("0")]
		[TestCase("1.5")]
		[TestCase("4,x")]
		public async Task SetPermissionData_refuses_role_lists_the_claim_chain_cannot_parse(string data)
		{
			Row(PermissionTypes.CreateCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles);

			var result = await _controller.SetPermissionData((int)PermissionTypes.CreateCall, data, null);

			StatusOf(result).Should().Be(400);
			VerifyNothingSaved();
		}

		[TestCase(" 7, 3,7,, ", "7,3")]
		[TestCase("", "")]
		[TestCase(null, "")]
		public async Task SetPermissionData_normalizes_the_role_list(string data, string stored)
		{
			Row(PermissionTypes.CreateCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles);

			var result = await _controller.SetPermissionData((int)PermissionTypes.CreateCall, data, null);

			StatusOf(result).Should().Be(200);
			VerifySaved(PermissionTypes.CreateCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles, stored, false);
		}

		[TestCase(PermissionTypes.AddPersonnel)]
		[TestCase(PermissionTypes.RemovePersonnel)]
		public async Task SetPermissionData_refuses_rows_without_a_roles_picker(PermissionTypes type)
		{
			Row(type, (int)PermissionActions.DepartmentAndGroupAdmins);

			var result = await _controller.SetPermissionData((int)type, "3", null);

			StatusOf(result).Should().Be(400);
			VerifyNothingSaved();
		}

		// ── Defect 2: the call rows' group lock ──────────────────────────────────────────────────────

		[Test]
		public async Task Index_preloads_the_saved_group_lock_on_the_call_rows()
		{
			Row(PermissionTypes.DeleteCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "4", lockToGroup: true);
			Row(PermissionTypes.CloseCall, (int)PermissionActions.DepartmentAndGroupAdmins, lockToGroup: true);
			Row(PermissionTypes.AddCallData, (int)PermissionActions.Everyone, lockToGroup: true);

			var model = await IndexModel();

			model.DeleteCall.Should().Be((int)PermissionActions.DepartmentAdminsAndSelectRoles);
			model.LockDeleteCallToGroup.Should().BeTrue();
			model.CloseCall.Should().Be((int)PermissionActions.DepartmentAndGroupAdmins);
			model.LockCloseCallToGroup.Should().BeTrue();
			model.AddCallData.Should().Be((int)PermissionActions.Everyone);
			model.LockAddCallDataToGroup.Should().BeTrue();
		}

		[Test]
		public async Task Index_shows_the_call_rows_unlocked_without_saved_rows()
		{
			var model = await IndexModel();

			model.LockDeleteCallToGroup.Should().BeFalse();
			model.LockCloseCallToGroup.Should().BeFalse();
			model.LockAddCallDataToGroup.Should().BeFalse();
		}

		[Test]
		public async Task SetPermission_keeps_the_saved_roles_and_lock_when_the_request_leaves_the_lock_out()
		{
			Row(PermissionTypes.DeleteCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "4,5", lockToGroup: true);

			var result = await _controller.SetPermission((int)PermissionTypes.DeleteCall, (int)PermissionActions.DepartmentAndGroupAdmins, null);

			StatusOf(result).Should().Be(200);
			VerifySaved(PermissionTypes.DeleteCall, (int)PermissionActions.DepartmentAndGroupAdmins, "4,5", true);
		}

		[TestCase(PermissionTypes.DeleteCall)]
		[TestCase(PermissionTypes.CloseCall)]
		[TestCase(PermissionTypes.AddCallData)]
		public async Task Toggling_the_group_lock_keeps_the_saved_roles(PermissionTypes type)
		{
			Row(type, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "4,5", lockToGroup: true);

			var result = await _controller.SetPermission((int)type, (int)PermissionActions.DepartmentAdminsAndSelectRoles, false);

			StatusOf(result).Should().Be(200);
			VerifySaved(type, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "4,5", false);
		}

		[Test]
		public async Task Changing_roles_keeps_the_saved_action_and_lock()
		{
			Row(PermissionTypes.CloseCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "4", lockToGroup: true);

			var result = await _controller.SetPermissionData((int)PermissionTypes.CloseCall, "4,6", null);

			StatusOf(result).Should().Be(200);
			VerifySaved(PermissionTypes.CloseCall, (int)PermissionActions.DepartmentAdminsAndSelectRoles, "4,6", true);
		}

		[Test]
		public async Task Changing_a_location_rule_still_refreshes_the_security_cache()
		{
			await _controller.SetPermission((int)PermissionTypes.CanSeeUnitLocations, (int)PermissionActions.DepartmentAdminsOnly, true);

			_events.Verify(e => e.SendMessage(It.Is<SecurityRefreshEvent>(x => x.DepartmentId == DepartmentId && x.Type == SecurityCacheTypes.WhoCanViewUnitLocations)), Times.Once);
			_events.Verify(e => e.SendMessage(It.Is<AuditEvent>(x => x.Type == AuditLogTypes.PermissionsChanged)), Times.Once);
		}

		[Test]
		public void Permissions_script_does_not_post_roles_back_on_load_and_sends_each_rows_own_lock()
		{
			var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "wwwroot", "js", "app", "internal", "security", "resgrid.security.permissions.js"));

			// Loading the saved roles must only redraw select2; a plain 'change' runs the SetPermissionData handler.
			script.Should().Contain("trigger('change.select2')");
			script.Should().NotContain("$(selector).trigger('change')");

			foreach (var (type, box) in new[] { (11, "LockViewPersonneLocationToGroup"), (12, "LockViewUnitLocationToGroup"), (14, "LockViewGroupsUsersToGroup"),
				(15, "LockDeleteCallToGroup"), (16, "LockCloseCallToGroup"), (17, "LockAddCallDataToGroup"), (18, "LockViewGroupsUnitsToGroup") })
			{
				Regex.IsMatch(script, $@"initPermRoles\(""#\w+"", {type}, function \(\) {{ return \$\('#{box}'\)\.is\(':checked'\); }}\)")
					.Should().BeTrue($"role writes for type {type} must carry the {box} state");

				foreach (Match post in Regex.Matches(script, $@"SetPermission\?type={type}&perm=[^\n]*"))
				{
					post.Value.Should().Contain($"#{box}').is(':checked')", $"every type {type} action write sends its own lock box");
					post.Value.Should().NotContain($"#{box}').val()", "the lock box's value is not an action");
				}
			}
		}

		// ── Defect 3: no-row preselects match the runtime ───────────────────────────────────────────

		[Test]
		public async Task Index_with_no_rows_preselects_what_the_runtime_applies_to_a_missing_row()
		{
			var model = await IndexModel();

			model.AdjustInventory.Should().Be((int)PermissionActions.DepartmentAdminsOnly, "InventoryAuthorizationService treats no row as department admins");
			model.CreateCall.Should().Be((int)PermissionActions.Everyone);

			foreach (var (value, options, key) in new[]
			{
				(model.CreateWorkflow, model.CreateWorkflowPermissions, PermissionScreenCatalog.NotSavedCreateWorkflowKey),
				(model.ManageWorkflowCredentials, model.ManageWorkflowCredentialsPermissions, PermissionScreenCatalog.NotSavedManageWorkflowCredentialsKey),
				(model.ViewWorkflowRuns, model.ViewWorkflowRunsPermissions, PermissionScreenCatalog.NotSavedViewWorkflowRunsKey)
			})
			{
				value.Should().Be(PermissionScreenCatalog.NotSavedValue, key);
				var first = options.First();
				first.Value.Should().Be("-1", key);
				first.Disabled.Should().BeTrue(key);
				first.Selected.Should().BeTrue(key);
				first.Text.Should().Be(key, "the localized not-saved label describes the missing-row behaviour");
				options.Skip(1).Should().OnlyContain(o => !o.Disabled && !o.Selected, key);
			}

			model.RecordsPermissions.Single(r => r.Type == PermissionTypes.ViewAllWorkOrders).LockToGroup.Should().BeTrue();
			model.RecordsPermissions.Single(r => r.Type == PermissionTypes.ViewChecklistResults).LockToGroup.Should().BeTrue();
			model.RecordsPermissions.Single(r => r.Type == PermissionTypes.ManageWorkOrders).LockToGroup.Should().BeFalse();
		}

		[Test]
		public async Task Index_drops_the_not_saved_option_once_a_workflow_rule_is_saved()
		{
			Row(PermissionTypes.CreateWorkflow, (int)PermissionActions.DepartmentAndGroupAdmins);

			var model = await IndexModel();

			model.CreateWorkflow.Should().Be((int)PermissionActions.DepartmentAndGroupAdmins);
			model.CreateWorkflowPermissions.Select(o => o.Value).Should().Equal("0", "1", "2");
			model.CreateWorkflowPermissions.Single(o => o.Selected).Value.Should().Be("1");
		}

		[Test]
		public async Task Adjust_inventory_screen_default_matches_inventory_authorization_and_claims()
		{
			PermissionScreenCatalog.Get(PermissionTypes.AdjustInventory).NoRowValue.Should().Be((int)PermissionActions.DepartmentAdminsOnly);

			var actor = new InventoryActor { DepartmentId = 77, UserId = "member" };
			var member = new DepartmentMember { DepartmentId = 77, UserId = actor.UserId };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, 77, true)).ReturnsAsync(() => member);
			departments.Setup(d => d.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "owner" });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(77, true)).ReturnsAsync(new DepartmentModuleSettings());
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForUserAsync(actor.UserId, 77)).ReturnsAsync(new List<PersonnelRole>());
			var service = new InventoryAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUnitsService>(), Mock.Of<IAuthorizationService>(),
				Mock.Of<IPermissionsService>(), roles.Object, settings.Object);

			await service.Invoking(s => s.RequireAsync(actor, true)).Should().ThrowAsync<InventoryException>("a member is refused with no saved rule");
			member.IsAdmin = true;
			await service.Invoking(s => s.RequireAsync(actor, true)).Should().NotThrowAsync();

			foreach (var admin in new[] { true, false })
			{
				var none = new ClaimsIdentity();
				ClaimsLogic.AddInventoryClaims(none, admin, new List<Permission>(), false, new List<PersonnelRole>());
				var saved = new ClaimsIdentity();
				ClaimsLogic.AddInventoryClaims(saved, admin, new List<Permission> { new Permission { PermissionType = (int)PermissionTypes.AdjustInventory, Action = (int)PermissionActions.DepartmentAdminsOnly } },
					false, new List<PersonnelRole>());

				saved.Claims.Select(c => c.Type + ":" + c.Value).Should().BeEquivalentTo(none.Claims.Select(c => c.Type + ":" + c.Value), $"admin={admin}");
			}
		}

		[TestCase(PermissionTypes.CreateWorkflow)]
		[TestCase(PermissionTypes.ManageWorkflowCredentials)]
		[TestCase(PermissionTypes.ViewWorkflowRuns)]
		public void No_offered_workflow_action_behaves_like_a_missing_row(PermissionTypes type)
		{
			// A missing row admits every member to the pages (IsUserAllowed(null)) with read claims only; each offered
			// action either keeps a plain member off the pages or hands out write claims, so the screen cannot preselect one.
			var permissions = new PermissionsService(Mock.Of<IPermissionsRepository>(), Mock.Of<IUsersService>(), Mock.Of<IDepartmentGroupsService>());
			var noRoles = new List<PersonnelRole>();
			permissions.IsUserAllowed(null, false, false, noRoles).Should().BeTrue();
			var noRowClaims = WorkflowClaims(type, null);

			var entry = PermissionScreenCatalog.Get(type);
			entry.NoRowValue.Should().Be(PermissionScreenCatalog.NotSavedValue);

			foreach (var action in entry.Actions)
			{
				var row = new Permission { PermissionType = (int)type, Action = action };
				var sameAccess = permissions.IsUserAllowed(row, false, false, noRoles) && WorkflowClaims(type, row).SetEquals(noRowClaims);
				sameAccess.Should().BeFalse($"action {action} must not look like the missing row");
			}
		}

		[Test]
		public async Task View_all_work_orders_screen_ticks_the_group_lock_the_service_applies()
		{
			var row = RecordsPermissionRows.Build(new List<Permission>(), WorkOrderPermissionCatalog.All).Single(r => r.Type == PermissionTypes.ViewAllWorkOrders);
			row.Value.Should().Be((int)PermissionActions.DepartmentAndGroupAdmins);
			row.LockToGroup.Should().BeTrue();

			var actor = new ChecklistActor { DepartmentId = 77, UserId = "group-admin" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, 77, true)).ReturnsAsync(new DepartmentMember { DepartmentId = 77, UserId = actor.UserId });
			departments.Setup(d => d.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "owner" });
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupForUserAsync(actor.UserId, 77)).ReturnsAsync(new DepartmentGroup
			{
				DepartmentId = 77, DepartmentGroupId = 5,
				Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = actor.UserId, IsAdmin = true } }
			});
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForUserAsync(actor.UserId, 77)).ReturnsAsync(new List<PersonnelRole>());
			var service = new WorkOrderAuthorizationService(departments.Object, groups.Object, roles.Object, Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(),
				Mock.Of<IAuthorizationService>(), Mock.Of<IChecklistAssignmentService>(), Mock.Of<IUserProfileService>());

			var scope = await service.ScopeAsync(actor);

			scope.All.Should().BeFalse("with no saved rule a group admin is held to their own group");
			scope.GroupId.Should().Be(5);
		}

		// ── Defect 4: only what the screen offers can be written ────────────────────────────────────

		[TestCase((int)PermissionTypes.BreakGlassProtectedData)]
		// Advanced Data Protection values no runtime check reads (2026-10-04 audit): removed from the screen, so refused.
		[TestCase((int)PermissionTypes.ManageDepartmentDataProtection)]
		[TestCase((int)PermissionTypes.EditProtectedCallData)]
		[TestCase((int)PermissionTypes.ViewProtectedPersonnelData)]
		[TestCase((int)PermissionTypes.ViewProtectedContactData)]
		[TestCase((int)PermissionTypes.ViewProtectedOperationalData)]
		[TestCase((int)PermissionTypes.ExportProtectedData)]
		// Records values whose claims no endpoint uses yet.
		[TestCase((int)PermissionTypes.ShareRecordsExternally)]
		[TestCase((int)PermissionTypes.ViewLegacyRecords)]
		[TestCase((int)PermissionTypes.ViewUdfFields)]
		[TestCase((int)PermissionTypes.ManageRoutes)]
		[TestCase(68)]
		[TestCase(999)]
		[TestCase(-5)]
		public async Task Types_the_screen_does_not_offer_are_refused(int type)
		{
			StatusOf(await _controller.SetPermission(type, (int)PermissionActions.DepartmentAdminsOnly, null)).Should().Be(400);
			StatusOf(await _controller.SetPermissionData(type, "3", null)).Should().Be(400);

			VerifyNothingSaved();
		}

		[TestCase(PermissionTypes.AddPersonnel, 2)]
		[TestCase(PermissionTypes.AddPersonnel, 3)]
		[TestCase(PermissionTypes.AddPersonnel, 4)]
		[TestCase(PermissionTypes.RemovePersonnel, 2)]
		[TestCase(PermissionTypes.CreateWorkflow, 3)]
		[TestCase(PermissionTypes.CreateWorkflow, -1)]
		[TestCase(PermissionTypes.CreateCall, 4)]
		[TestCase(PermissionTypes.CreateCall, 7)]
		[TestCase(PermissionTypes.ConfigureProtectedDataEgress, 3)]
		[TestCase(PermissionTypes.SubmitRecords, 3)]
		public async Task Actions_the_row_does_not_offer_are_refused(PermissionTypes type, int action)
		{
			var result = await _controller.SetPermission((int)type, action, null);

			StatusOf(result).Should().Be(400);
			VerifyNothingSaved();
		}

		[TestCase(PermissionTypes.CreateRecord)]
		[TestCase(PermissionTypes.DeleteRecord)]
		[TestCase(PermissionTypes.ReviewRecords)]
		[TestCase(PermissionTypes.ApproveRecords)]
		[TestCase(PermissionTypes.FinalizeRecords)]
		[TestCase(PermissionTypes.AmendRecords)]
		[TestCase(PermissionTypes.ExportRecords)]
		[TestCase(PermissionTypes.ViewRestrictedRecords)]
		[TestCase(PermissionTypes.ReassignRecordDrafts)]
		public async Task A_group_lock_on_a_Records_action_that_never_evaluates_it_is_refused(PermissionTypes type)
		{
			PermissionScreenCatalog.Get(type).LockToGroupOffered.Should().BeFalse();

			StatusOf(await _controller.SetPermission((int)type, (int)PermissionActions.DepartmentAndGroupAdmins, true)).Should().Be(400);
			StatusOf(await _controller.SetPermissionData((int)type, "3", true)).Should().Be(400);

			VerifyNothingSaved();
		}

		[Test]
		public async Task View_group_records_keeps_its_group_lock()
		{
			PermissionScreenCatalog.Get(PermissionTypes.ViewGroupRecords).LockToGroupOffered.Should().BeTrue();

			StatusOf(await _controller.SetPermission((int)PermissionTypes.ViewGroupRecords, (int)PermissionActions.DepartmentAndGroupAdmins, true)).Should().Be(200);

			VerifySaved(PermissionTypes.ViewGroupRecords, (int)PermissionActions.DepartmentAndGroupAdmins, null, true);
		}

		[Test]
		public async Task Index_offers_only_the_enforced_Advanced_Data_Protection_rows()
		{
			var model = await IndexModel();

			model.ViewProtectedCallDataPermissions.Should().NotBeEmpty();
			model.ConfigureProtectedDataEgressPermissions.Should().NotBeEmpty();
			PermissionScreenCatalog.All.Select(e => e.Type).Where(t => (int)t >= 31 && (int)t <= 39)
				.Should().BeEquivalentTo(new[] { PermissionTypes.ViewProtectedCallData, PermissionTypes.ConfigureProtectedDataEgress });
			model.RecordsPermissions.Select(r => r.Type).Should().NotContain(new[] { PermissionTypes.ShareRecordsExternally, PermissionTypes.ViewLegacyRecords });
		}

		[Test]
		public async Task Every_action_the_screen_offers_is_accepted()
		{
			foreach (var entry in PermissionScreenCatalog.All)
			{
				foreach (var action in entry.Actions)
				{
					_permissions.Invocations.Clear();

					var result = await _controller.SetPermission((int)entry.Type, action, null);

					StatusOf(result).Should().Be(200, $"{entry.Type} action {action}");
					VerifySaved(entry.Type, action, null, entry.LockToGroupOffered && entry.NoRowLockToGroup);
				}
			}
		}

		[TestCase(PermissionTypes.CreateCall)]
		[TestCase(PermissionTypes.AddPersonnel)]
		[TestCase(PermissionTypes.SubmitRecords)]
		public async Task A_group_lock_on_a_row_without_the_box_is_refused(PermissionTypes type)
		{
			var action = PermissionScreenCatalog.Get(type).Actions.First();

			StatusOf(await _controller.SetPermission((int)type, action, true)).Should().Be(400);
			StatusOf(await _controller.SetPermission((int)type, action, false)).Should().Be(200);
		}

		[Test]
		public async Task A_saved_value_outside_the_offer_stays_selectable()
		{
			// RecordsPermissionRows lists a stored value the catalog would not offer, so re-choosing it widens nothing.
			Row(PermissionTypes.SubmitRecords, (int)PermissionActions.Everyone);

			var result = await _controller.SetPermission((int)PermissionTypes.SubmitRecords, (int)PermissionActions.Everyone, null);

			StatusOf(result).Should().Be(200);
		}

		[Test]
		public async Task An_unbindable_action_is_refused_instead_of_saving_the_default()
		{
			// perm=true used to bind as 0 and silently narrow the rule to department admins.
			_controller.ModelState.AddModelError("perm", "The value 'true' is not valid.");

			StatusOf(await _controller.SetPermission((int)PermissionTypes.ViewGroupUsers, 0, true)).Should().Be(400);
			VerifyNothingSaved();
		}

		[Test]
		public async Task Non_admins_write_nothing()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor
			{
				HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "member") }, "test")) }
			};

			StatusOf(await _controller.SetPermission((int)PermissionTypes.CreateCall, 0, null)).Should().Be(304);
			StatusOf(await _controller.SetPermissionData((int)PermissionTypes.CreateCall, "3", null)).Should().Be(304);
			VerifyNothingSaved();
		}

		[Test]
		public void The_permissions_page_posts_exactly_the_types_the_catalog_offers()
		{
			// Fixed and ADP rows are wired by hand in the script; Records rows come from the catalog through data attributes.
			var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "wwwroot", "js", "app", "internal", "security", "resgrid.security.permissions.js"));
			var posted = Regex.Matches(script, @"SetPermission\?type=(\d+)").Select(m => int.Parse(m.Groups[1].Value))
				.Concat(Regex.Matches(script, @"\btype: (\d+)").Select(m => int.Parse(m.Groups[1].Value)))
				.ToHashSet();
			var recordTypes = PermissionScreenCatalog.RecordCatalogs.SelectMany(c => c).Select(d => (int)d.Type).ToHashSet();

			posted.Should().BeEquivalentTo(PermissionScreenCatalog.All.Select(e => (int)e.Type).Where(t => !recordTypes.Contains(t)));
		}

		[Test]
		public void Not_saved_labels_exist_in_every_language()
		{
			var directory = Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Security");
			foreach (var culture in Resgrid.Localization.SupportedLocales.GetSupportedCultures())
			{
				var keys = System.Xml.Linq.XDocument.Load(Path.Combine(directory, $"Security.{culture}.resx")).Root!.Elements("data")
					.Where(d => !string.IsNullOrWhiteSpace(d.Element("value")?.Value)).Select(d => d.Attribute("name")!.Value).ToHashSet();

				keys.Should().Contain(PermissionScreenCatalog.NotSavedLabelKeys, culture);
				PermissionScreenCatalog.All.Where(e => e.NoRowValue == PermissionScreenCatalog.NotSavedValue)
					.Select(e => e.NotSavedLabelKey).Should().OnlyContain(k => keys.Contains(k), culture);
			}
		}

		// ── helpers ────────────────────────────────────────────────────────────────────────────────

		private void Row(PermissionTypes type, int action, string data = null, bool lockToGroup = false) =>
			_rows.Add(new Permission { DepartmentId = DepartmentId, PermissionType = (int)type, Action = action, Data = data, LockToGroup = lockToGroup });

		private void VerifySaved(PermissionTypes type, int action, string data, bool lockToGroup) =>
			_permissions.Verify(p => p.SetPermissionForDepartmentAsync(DepartmentId, UserId, type, (PermissionActions)action, data, lockToGroup, It.IsAny<CancellationToken>()),
				Times.Once, $"{type} action {action} data '{data}' lock {lockToGroup}");

		private void VerifyNothingSaved() =>
			_permissions.Verify(p => p.SetPermissionForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<PermissionTypes>(), It.IsAny<PermissionActions>(),
				It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

		private static int? StatusOf(IActionResult result) => (result as StatusCodeResult)?.StatusCode;

		private async Task<PermissionsView> IndexModel() =>
			(await _controller.Index()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<PermissionsView>().Subject;

		private static HashSet<string> WorkflowClaims(PermissionTypes type, Permission row)
		{
			var identity = new ClaimsIdentity();
			var rows = row == null ? new List<Permission>() : new List<Permission> { row };
			switch (type)
			{
				case PermissionTypes.CreateWorkflow: ClaimsLogic.AddWorkflowClaims(identity, false, rows, false, new List<PersonnelRole>()); break;
				case PermissionTypes.ManageWorkflowCredentials: ClaimsLogic.AddWorkflowCredentialClaims(identity, false, rows, false, new List<PersonnelRole>()); break;
				default: ClaimsLogic.AddWorkflowRunClaims(identity, false, rows, false, new List<PersonnelRole>()); break;
			}

			return identity.Claims.Select(c => c.Type + ":" + c.Value).ToHashSet();
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
		}
	}
}
