using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Search;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using MvcClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using V4 = Resgrid.Web.Services.Controllers.v4;
using WebProtocols = Resgrid.Web.Areas.User.Controllers.ProtocolsController;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The protocol read rule (R-P4) holds on every path that hands out protocols: the access service itself, v4
	/// GetAllProtocols / GetProtocol / GetFile, v4 Sync Reference, the web library and detail page, the Unified Search hit check
	/// (AuthorizationService.CanUserViewProtocolAsync), the search projection, and the per-version rule. One REAL
	/// ProtocolAccessService over mocked rows decides on every path, so a plain member sees exactly the whole-department
	/// protocol and their own group's protocol: never the draft, never another audience's protocol, never a Dispatch protocol
	/// outside their audience unless they work dispatch. Managers (department admin, owner, Author 128, Approve 129) read all,
	/// drafts included; a member who is not current reads nothing.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ProtocolAudienceGuardTests
	{
		private const int DepartmentId = 10;
		private const int OtherDepartmentId = 99;

		private const string Owner = "owner";
		private const string Admin = "admin";
		private const string Member = "member";
		private const string Hidden = "hidden";
		private const string Dispatcher = "dispatcher";
		private const string Author = "author";
		private const string Approver = "approver";
		private const string Acknowledger = "acknowledger";
		private const string Disabled = "disabled";
		private const string Deleted = "deleted";
		private const string Stranger = "stranger";

		private const int MemberGroupId = 5;
		private const int OtherGroupId = 6;
		private const int AuthorRoleId = 7;
		private const int ApproverRoleId = 8;
		private const int AcknowledgerRoleId = 9;

		// P1 whole department, P2 another group, P3 draft, P4 Dispatch kind for another group, P5 the member's group, P6 another department.
		private const int P1 = 1, P2 = 2, P3 = 3, P4 = 4, P5 = 5, P6 = 6;

		private List<DispatchProtocol> _protocols;
		private List<ProtocolAudience> _audiences;
		private List<ProtocolVersion> _versions;
		private Mock<IDepartmentsService> _departments;
		private Mock<IPermissionsService> _permissions;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IContentAudienceService> _contentAudience;
		private Mock<IDispatchAccessService> _dispatch;
		private Mock<IProtocolAudiencesRepository> _audienceRows;
		private Mock<IDispatchProtocolRepository> _protocolRows;
		private Mock<IProtocolsService> _protocolsService;
		private Mock<IProtocolLibraryService> _library;
		private ProtocolAccessService _access;

		[SetUp]
		public void SetUp()
		{
			_protocols = new List<DispatchProtocol>
			{
				Protocol(P1, ProtocolKinds.Sog, ProtocolStatuses.Published, 101),
				Protocol(P2, ProtocolKinds.Policy, ProtocolStatuses.Published, 102),
				Protocol(P3, ProtocolKinds.Sog, ProtocolStatuses.Draft, null),
				Protocol(P4, ProtocolKinds.Dispatch, ProtocolStatuses.Published, 104),
				Protocol(P5, ProtocolKinds.Sog, ProtocolStatuses.Published, 105),
				Protocol(P6, ProtocolKinds.Sog, ProtocolStatuses.Published, 106, OtherDepartmentId)
			};
			_audiences = new List<ProtocolAudience>
			{
				Audience(P2, ContentAudienceKinds.Group, OtherGroupId.ToString()),
				Audience(P4, ContentAudienceKinds.Group, OtherGroupId.ToString()),
				Audience(P5, ContentAudienceKinds.Group, MemberGroupId.ToString())
			};
			_versions = new List<ProtocolVersion>
			{
				Version(101, P1, ProtocolVersionStatuses.Published),
				Version(102, P2, ProtocolVersionStatuses.Published),
				Version(103, P3, ProtocolVersionStatuses.Draft),
				Version(104, P4, ProtocolVersionStatuses.Published),
				Version(105, P5, ProtocolVersionStatuses.Published)
			};

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => new Department
			{
				DepartmentId = DepartmentId, ManagingUserId = Owner, AdminUsers = new List<string> { Admin }
			});
			_departments.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int departmentId, bool _) => userId == Stranger ? null : new DepartmentMember
				{
					DepartmentId = departmentId, UserId = userId, IsDisabled = userId == Disabled, IsDeleted = userId == Deleted, IsHidden = userId == Hidden
				});
			// Unified Search resolves the caller's department from the user.
			_departments.Setup(x => x.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(() => new Department
			{
				DepartmentId = DepartmentId, ManagingUserId = Owner, AdminUsers = new List<string> { Admin }
			});

			// Author 128 and Approve 129 for selected roles; Manage Acknowledgments 130 for a third role (it never opens drafts).
			_permissions = new Mock<IPermissionsService>();
			_permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.AuthorProtocols))
				.ReturnsAsync(Row(PermissionTypes.AuthorProtocols, AuthorRoleId));
			_permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ApproveProtocols))
				.ReturnsAsync(Row(PermissionTypes.ApproveProtocols, ApproverRoleId));
			_permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ManageProtocolAcknowledgments))
				.ReturnsAsync(Row(PermissionTypes.ManageProtocolAcknowledgments, AcknowledgerRoleId));

			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync((string userId, int departmentId) => userId switch
			{
				Author => new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = AuthorRoleId, DepartmentId = departmentId } },
				Approver => new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = ApproverRoleId, DepartmentId = departmentId } },
				Acknowledger => new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = AcknowledgerRoleId, DepartmentId = departmentId } },
				_ => new List<PersonnelRole>()
			});

			// The member and the hidden member belong to group 5; the shared resolver marks a hidden member inactive.
			_contentAudience = new Mock<IContentAudienceService>();
			_contentAudience.Setup(x => x.GetMemberFactsAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((int _, string userId) => new ContentAudienceMemberFacts
			{
				UserId = userId,
				IsActive = userId != Hidden,
				GroupIds = userId == Member || userId == Hidden ? new HashSet<int> { MemberGroupId } : new HashSet<int>()
			});

			_dispatch = new Mock<IDispatchAccessService>();
			_dispatch.Setup(x => x.CanUseDispatchAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((int _, string userId) => userId == Dispatcher);

			_audienceRows = new Mock<IProtocolAudiencesRepository>();
			_audienceRows.Setup(x => x.GetForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int departmentId) => _audiences.Where(a => a.DepartmentId == departmentId).ToList());
			_audienceRows.Setup(x => x.GetForProtocolAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int departmentId, int protocolId) => _audiences.Where(a => a.DepartmentId == departmentId && a.DispatchProtocolId == protocolId).ToList());

			_protocolRows = new Mock<IDispatchProtocolRepository>();
			_protocolRows.Setup(x => x.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => ById((int)id));

			_access = new ProtocolAccessService(_departments.Object, _permissions.Object, Mock.Of<IDepartmentGroupsService>(), _roles.Object,
				_contentAudience.Object, _dispatch.Object, _audienceRows.Object, _protocolRows.Object);

			_protocolsService = new Mock<IProtocolsService>();
			_protocolsService.Setup(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(() => _protocols.Where(p => p.DepartmentId == DepartmentId).ToList());
			_protocolsService.Setup(x => x.GetProtocolByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => ById(id));
			_protocolsService.Setup(x => x.GetProtocolHeaderByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => ById(id));
			_protocolsService.Setup(x => x.GetUsageCountsAsync(DepartmentId, It.IsAny<DateTime>())).ReturnsAsync(new Dictionary<int, int>());
			_protocolsService.Setup(x => x.GetAttachmentForDepartmentAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((int _, int attachmentId) =>
				new DispatchProtocolAttachment
				{
					// Attachment n0 belongs to protocol n (20 is P2's, 30 is P3's, 10 is P1's).
					DispatchProtocolAttachmentId = attachmentId, DispatchProtocolId = attachmentId / 10, FileName = "sop.pdf", FileType = "application/pdf",
					Data = new byte[] { 1, 2, 3 }
				});

			// The library's reads return empty lists, except the version headers and a protocol's own versions.
			_library = new Mock<IProtocolLibraryService>();
			_library.Setup(x => x.GetDepartmentVersionHeadersAsync(DepartmentId)).ReturnsAsync(() => _versions.ToList());
			_library.Setup(x => x.GetDepartmentAudiencesAsync(DepartmentId)).ReturnsAsync(() => _audiences.ToList());
			_library.Setup(x => x.GetCategoriesAsync(DepartmentId)).ReturnsAsync(new List<ProtocolCategory>());
			_library.Setup(x => x.GetFavoritesAndRecentAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((new List<int>(), new List<int>()));
			_library.Setup(x => x.GetAudienceAsync(DepartmentId, It.IsAny<int>()))
				.ReturnsAsync((int _, int protocolId) => _audiences.Where(a => a.DispatchProtocolId == protocolId).ToList());
			_library.Setup(x => x.GetVersionsAsync(DepartmentId, It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int _, int protocolId, bool __) => _versions.Where(v => v.DispatchProtocolId == protocolId).ToList());
			_library.Setup(x => x.GetVersionAsync(DepartmentId, It.IsAny<int>()))
				.ReturnsAsync((int _, int versionId) => _versions.FirstOrDefault(v => v.ProtocolVersionId == versionId));
			_library.Setup(x => x.GetCommentsAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync(new List<ProtocolComment>());
			_library.Setup(x => x.GetRecentCallsAsync(DepartmentId, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolCallUsage>());
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = null;
			MvcClaims._httpContextAccessor = null;
		}

		#region Fixture

		private static DispatchProtocol Protocol(int id, ProtocolKinds kind, ProtocolStatuses status, int? currentVersionId, int departmentId = DepartmentId) => new DispatchProtocol
		{
			DispatchProtocolId = id, DepartmentId = departmentId, Name = "Protocol " + id, Code = "P" + id, Kind = (int)kind, Status = (int)status,
			CurrentVersionId = currentVersionId, ProtocolText = "Text of protocol " + id, CreatedOn = new DateTime(2026, 1, 1), CreatedByUserId = Owner, UpdatedByUserId = Owner
		};

		private static ProtocolAudience Audience(int protocolId, ContentAudienceKinds kind, string refId) => new ProtocolAudience
		{
			DepartmentId = DepartmentId, DispatchProtocolId = protocolId, Kind = (int)kind, RefId = refId
		};

		private static ProtocolVersion Version(int id, int protocolId, ProtocolVersionStatuses status) => new ProtocolVersion
		{
			ProtocolVersionId = id, DispatchProtocolId = protocolId, DepartmentId = DepartmentId, Status = (int)status, Major = 1,
			ContentHtml = "<h2>Scope</h2><p>Protocol " + protocolId + "</p>"
		};

		private static Permission Row(PermissionTypes type, int roleId) => new Permission
		{
			DepartmentId = DepartmentId, PermissionType = (int)type, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = roleId.ToString()
		};

		private DispatchProtocol ById(int id) => _protocols.FirstOrDefault(p => p.DispatchProtocolId == id);

		private List<DispatchProtocol> All => _protocols.ToList();

		private static DefaultHttpContext SignIn(string userId)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			MvcClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		private V4.ProtocolsController ApiProtocols(string userId) =>
			new V4.ProtocolsController(_protocolsService.Object, Mock.Of<IAuthorizationService>(), Mock.Of<ICallsService>(), Mock.Of<IEventAggregator>(), _access)
			{
				ControllerContext = new ControllerContext { HttpContext = SignIn(userId) }
			};

		private V4.SyncController Sync(string userId, ISyncService sync, IAuthorizationService authorization) =>
			new V4.SyncController(Mock.Of<IIncidentCommandService>(), Mock.Of<IIncidentResourcesService>(), sync, authorization, Mock.Of<ICommandAccessService>(), _access)
			{
				ControllerContext = new ControllerContext { HttpContext = SignIn(userId) }
			};

		private WebProtocols WebLibrary(string userId)
		{
			var http = SignIn(userId);
			var controller = new WebProtocols(_protocolsService.Object, Mock.Of<ICallsService>(), Mock.Of<IAuthorizationService>(), _departments.Object, Mock.Of<IMappingService>(), Mock.Of<IEventAggregator>(),
				access: _access, library: _library.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		private AuthorizationService Authorization() => new AuthorizationService(_departments.Object, Mock.Of<IInvitesService>(), Mock.Of<ICallsService>(),
			Mock.Of<IMessageService>(), Mock.Of<IWorkLogsService>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentGroupsService>(),
			_roles.Object, Mock.Of<IUnitsService>(), _permissions.Object, Mock.Of<ICalendarService>(), _protocolsService.Object,
			Mock.Of<IShiftsService>(), Mock.Of<ICustomStateService>(), Mock.Of<ICertificationService>(), Mock.Of<IDocumentsService>(),
			Mock.Of<INotesService>(), Mock.Of<ICacheProvider>(), Mock.Of<IContactsService>(), Mock.Of<IEventAggregator>(), Mock.Of<IDispatchScopeService>(),
			new Lazy<IProtocolAccessService>(() => _access));

		private async Task<List<int>> ReadableIdsAsync(string userId) =>
			(await _access.FilterReadableAsync(await _access.GetReaderAsync(DepartmentId, userId), All)).Select(p => p.DispatchProtocolId).ToList();

		#endregion Fixture

		#region a. The access service

		[Test]
		public async Task A_plain_member_reads_exactly_the_whole_department_protocol_and_their_own_groups_protocol()
		{
			(await ReadableIdsAsync(Member)).Should().Equal(P1, P5);
		}

		[TestCase(P1, true)]
		[TestCase(P2, false)]
		[TestCase(P3, false)]
		[TestCase(P4, false)]
		[TestCase(P5, true)]
		[TestCase(P6, false)]
		public async Task Can_read_and_get_readable_give_a_plain_member_the_same_answer_as_the_filter(int protocolId, bool readable)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, Member);

			(await _access.CanReadAsync(reader, ById(protocolId))).Should().Be(readable);
			var got = await _access.GetReadableAsync(DepartmentId, Member, protocolId);
			if (readable)
			{
				got.Should().NotBeNull();
				got.DispatchProtocolId.Should().Be(protocolId);
			}
			else
			{
				got.Should().BeNull();
			}
		}

		[Test]
		public async Task A_dispatcher_reads_the_dispatch_protocol_outside_their_audience_but_not_another_audiences_sog_or_the_draft()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, Dispatcher);

			reader.IsDispatcher.Should().BeTrue();
			reader.IsManager.Should().BeFalse();
			(await ReadableIdsAsync(Dispatcher)).Should().Equal(P1, P4);
			(await _access.CanReadAsync(reader, ById(P4))).Should().BeTrue();
			(await _access.GetReadableAsync(DepartmentId, Dispatcher, P4)).Should().NotBeNull();
		}

		[TestCase(Admin)]
		[TestCase(Owner)]
		[TestCase(Author)]
		[TestCase(Approver)]
		public async Task A_manager_reads_every_protocol_of_the_department_including_the_draft(string userId)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, userId);

			reader.IsManager.Should().BeTrue();
			(await ReadableIdsAsync(userId)).Should().Equal(P1, P2, P3, P4, P5);
			(await _access.CanReadAsync(reader, ById(P3))).Should().BeTrue();
			(await _access.GetReadableAsync(DepartmentId, userId, P3)).Should().NotBeNull();
			(await _access.CanReadAsync(reader, ById(P6))).Should().BeFalse("another department's protocol is never readable");
		}

		[Test]
		public async Task Author_and_approver_rights_come_from_the_permission_rows_not_from_holding_any_role()
		{
			var author = await _access.GetReaderAsync(DepartmentId, Author);
			var approver = await _access.GetReaderAsync(DepartmentId, Approver);

			author.CanAuthor.Should().BeTrue();
			author.CanApprove.Should().BeFalse();
			approver.CanApprove.Should().BeTrue();
			approver.CanAuthor.Should().BeFalse();
		}

		[Test]
		public async Task Manage_acknowledgments_alone_does_not_open_drafts_or_other_audiences()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, Acknowledger);

			reader.CanManageAcknowledgments.Should().BeTrue();
			reader.IsManager.Should().BeFalse();
			(await ReadableIdsAsync(Acknowledger)).Should().Equal(P1);
		}

		[TestCase(Disabled)]
		[TestCase(Deleted)]
		[TestCase(Stranger)]
		public async Task A_member_who_is_not_current_reads_nothing(string userId)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, userId);

			reader.IsCurrentMember.Should().BeFalse();
			(await ReadableIdsAsync(userId)).Should().BeEmpty();
			(await _access.CanReadAsync(reader, ById(P1))).Should().BeFalse();
			(await _access.GetReadableAsync(DepartmentId, userId, P1)).Should().BeNull();
		}

		[Test]
		public async Task A_hidden_but_current_member_still_reads_what_their_audience_gives_them()
		{
			(await ReadableIdsAsync(Hidden)).Should().Equal(P1, P5);
		}

		[Test]
		public async Task When_dispatch_access_cannot_be_read_the_member_keeps_only_their_audience()
		{
			_dispatch.Setup(x => x.CanUseDispatchAsync(DepartmentId, Dispatcher)).ThrowsAsync(new InvalidOperationException("dispatch access down"));

			(await ReadableIdsAsync(Dispatcher)).Should().Equal(P1);
		}

		#endregion a. The access service

		#region b. v4 ProtocolsController

		[Test]
		public async Task V4_get_all_protocols_returns_only_the_members_readable_ids()
		{
			var result = (await ApiProtocols(Member).GetAllProtocols()).Value;

			result.Data.Select(x => x.ProtocolId).Should().Equal(P1.ToString(), P5.ToString());
		}

		[Test]
		public async Task V4_get_all_protocols_gives_a_dispatcher_the_dispatch_protocol_and_a_manager_the_draft()
		{
			(await ApiProtocols(Dispatcher).GetAllProtocols()).Value.Data.Select(x => x.ProtocolId).Should().Equal(P1.ToString(), P4.ToString());
			(await ApiProtocols(Admin).GetAllProtocols()).Value.Data.Select(x => x.ProtocolId).Should().Equal("1", "2", "3", "4", "5");
		}

		[TestCase(P2)]
		[TestCase(P3)]
		[TestCase(P4)]
		public async Task V4_get_protocol_of_a_draft_or_out_of_audience_protocol_is_a_404_for_a_member(int protocolId)
		{
			(await ApiProtocols(Member).GetProtocol(protocolId)).Result.Should().BeOfType<NotFoundResult>();
		}

		[TestCase(P1)]
		[TestCase(P5)]
		public async Task V4_get_protocol_of_a_readable_protocol_is_ok_for_a_member(int protocolId)
		{
			var ok = (await ApiProtocols(Member).GetProtocol(protocolId)).Result.Should().BeOfType<OkObjectResult>().Subject;

			ok.Value.As<Resgrid.Web.Services.Models.v4.Protocols.GetProtocolResult>().Data.ProtocolId.Should().Be(protocolId.ToString());
		}

		[Test]
		public async Task V4_get_protocol_opens_the_dispatch_protocol_to_a_dispatcher_and_the_draft_to_an_admin()
		{
			(await ApiProtocols(Dispatcher).GetProtocol(P4)).Result.Should().BeOfType<OkObjectResult>();
			(await ApiProtocols(Admin).GetProtocol(P3)).Result.Should().BeOfType<OkObjectResult>();
		}

		[TestCase(20)]
		[TestCase(30)]
		[TestCase(40)]
		public async Task V4_get_file_of_an_attachment_on_a_protocol_the_member_may_not_read_is_a_404(int attachmentId)
		{
			(await ApiProtocols(Member).GetFile(attachmentId)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task V4_get_file_of_an_attachment_on_a_readable_protocol_returns_the_bytes()
		{
			var file = (await ApiProtocols(Member).GetFile(10)).Should().BeOfType<FileContentResult>().Subject;

			file.FileContents.Should().Equal(1, 2, 3);
		}

		#endregion b. v4 ProtocolsController

		#region c. v4 Sync Reference

		private (Mock<ISyncService> Sync, Mock<IAuthorizationService> Authorization) CachedReference()
		{
			var sync = new Mock<ISyncService>();
			// The cached, caller-agnostic build. A stale entry still carrying the draft must not reach anyone either.
			sync.Setup(x => x.GetReferenceDataAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => new SyncReferenceData
			{
				Protocols = _protocols.Where(p => p.DepartmentId == DepartmentId).ToList()
			});
			return (sync, new Mock<IAuthorizationService>());
		}

		[Test]
		public async Task Sync_reference_filters_the_cached_protocols_per_caller()
		{
			var (sync, authorization) = CachedReference();

			var member = (await Sync(Member, sync.Object, authorization.Object).Reference()).Value;
			var dispatcher = (await Sync(Dispatcher, sync.Object, authorization.Object).Reference()).Value;
			var disabled = (await Sync(Disabled, sync.Object, authorization.Object).Reference()).Value;

			member.Data.Protocols.Select(p => p.DispatchProtocolId).Should().Equal(P1, P5);
			dispatcher.Data.Protocols.Select(p => p.DispatchProtocolId).Should().Equal(P1, P4);
			disabled.Data.Protocols.Should().BeEmpty();
		}

		[Test]
		public async Task Sync_reference_never_carries_a_draft_even_for_a_manager()
		{
			var (sync, authorization) = CachedReference();

			var admin = (await Sync(Admin, sync.Object, authorization.Object).Reference()).Value;

			admin.Data.Protocols.Select(p => p.DispatchProtocolId).Should().Equal(P1, P2, P4, P5);
		}

		#endregion c. v4 Sync Reference

		#region d. Web library and detail

		[Test]
		public async Task Web_library_rows_hold_only_the_members_readable_protocols()
		{
			var model = (await WebLibrary(Member).Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ProtocolLibraryModel>().Subject;

			model.Rows.Select(r => r.Protocol.DispatchProtocolId).Should().Equal(P1, P5);
			model.Total.Should().Be(2);
			model.IsManager.Should().BeFalse();
			model.Rows.Should().OnlyContain(r => r.Open == null, "a member never sees another person's working copy");
		}

		[Test]
		public async Task Web_library_rows_give_a_manager_the_draft_too()
		{
			var model = (await WebLibrary(Admin).Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ProtocolLibraryModel>().Subject;

			model.Rows.Select(r => r.Protocol.DispatchProtocolId).Should().Equal(P1, P2, P3, P4, P5);
			model.Rows.Single(r => r.Protocol.DispatchProtocolId == P3).StatusKey.Should().Be("draft");
		}

		[TestCase(P2)]
		[TestCase(P3)]
		[TestCase(P4)]
		[TestCase(P6)]
		public async Task Web_view_of_a_draft_or_out_of_audience_protocol_is_a_404_for_a_member(int protocolId)
		{
			(await WebLibrary(Member).View(protocolId)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Web_view_of_a_readable_protocol_shows_the_page()
		{
			var model = (await WebLibrary(Member).View(P1)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ProtocolDetailModel>().Subject;

			model.Protocol.DispatchProtocolId.Should().Be(P1);
			model.Versions.Select(v => v.ProtocolVersionId).Should().Equal(101);
		}

		#endregion d. Web library and detail

		#region e. Unified Search hit check

		[TestCase(P1, true)]
		[TestCase(P2, false)]
		[TestCase(P3, false)]
		[TestCase(P4, false)]
		[TestCase(P5, true)]
		[TestCase(P6, false)]
		public async Task Authorization_service_answers_the_search_hit_check_with_the_same_rule(int protocolId, bool readable)
		{
			(await Authorization().CanUserViewProtocolAsync(Member, protocolId)).Should().Be(readable);
		}

		[Test]
		public async Task Authorization_service_lets_a_dispatcher_hit_the_dispatch_protocol_and_an_author_hit_the_draft()
		{
			var service = Authorization();

			(await service.CanUserViewProtocolAsync(Dispatcher, P4)).Should().BeTrue();
			(await service.CanUserViewProtocolAsync(Author, P3)).Should().BeTrue();
			(await service.CanUserViewProtocolAsync(Disabled, P1)).Should().BeFalse();
		}

		#endregion e. Unified Search hit check

		#region f. Search projection

		private SearchProjectionService Projection() =>
			new SearchProjectionService(Mock.Of<ISearchProjectionsRepository>(), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<Resgrid.Model.Repositories.IProtocolVersionTranslationsRepository>(), protocolAudiences: _audienceRows.Object);

		private static Dictionary<string, string> Metadata(SearchProjection projection) =>
			JsonConvert.DeserializeObject<Dictionary<string, string>>(projection.MetadataJson);

		[Test]
		public async Task Search_never_indexes_an_unpublished_protocol()
		{
			var service = Projection();
			var retired = Protocol(7, ProtocolKinds.Sog, ProtocolStatuses.Retired, 107);
			var deleted = Protocol(8, ProtocolKinds.Sog, ProtocolStatuses.Published, 108);
			deleted.IsDeleted = true;
			var publishedWithoutVersion = Protocol(9, ProtocolKinds.Sog, ProtocolStatuses.Published, null);

			(await service.BuildProtocolAsync(ById(P3))).Should().BeNull();
			(await service.BuildProtocolAsync(retired)).Should().BeNull();
			(await service.BuildProtocolAsync(deleted)).Should().BeNull();
			(await service.BuildProtocolAsync(publishedWithoutVersion)).Should().BeNull();
		}

		[Test]
		public async Task Search_indexes_a_published_protocol_with_its_audience_in_the_metadata()
		{
			var service = Projection();

			var everyone = Metadata(await service.BuildProtocolAsync(ById(P1)));
			var restricted = Metadata(await service.BuildProtocolAsync(ById(P2)));
			var dispatch = Metadata(await service.BuildProtocolAsync(ById(P4)));

			everyone["Audience"].Should().Be("Everyone");
			everyone["Status"].Should().Be("Published");
			restricted["Audience"].Should().Be((int)ContentAudienceKinds.Group + ":" + OtherGroupId);
			restricted["VersionId"].Should().Be("102");
			dispatch["Kind"].Should().Be("Dispatch");
		}

		#endregion f. Search projection

		#region g. Versions

		[TestCase(ProtocolVersionStatuses.Draft, false)]
		[TestCase(ProtocolVersionStatuses.InReview, false)]
		[TestCase(ProtocolVersionStatuses.Approved, false)]
		[TestCase(ProtocolVersionStatuses.Legacy, false)]
		[TestCase(ProtocolVersionStatuses.Published, true)]
		[TestCase(ProtocolVersionStatuses.Superseded, true)]
		[TestCase(ProtocolVersionStatuses.Retired, true)]
		public async Task A_member_reads_only_published_superseded_and_retired_versions_of_a_readable_protocol(ProtocolVersionStatuses status, bool readable)
		{
			var member = await _access.GetReaderAsync(DepartmentId, Member);
			var admin = await _access.GetReaderAsync(DepartmentId, Admin);
			var version = Version(200, P1, status);

			ProtocolAccessRules.CanReadVersion(member, ById(P1), new List<ProtocolAudience>(), version).Should().Be(readable);
			ProtocolAccessRules.CanReadVersion(admin, ById(P1), new List<ProtocolAudience>(), version).Should().BeTrue("a manager reads every version");
		}

		[Test]
		public async Task A_member_reads_no_version_of_a_protocol_outside_their_audience_or_of_another_protocol()
		{
			var member = await _access.GetReaderAsync(DepartmentId, Member);
			var p2Audience = _audiences.Where(a => a.DispatchProtocolId == P2).ToList();

			ProtocolAccessRules.CanReadVersion(member, ById(P2), p2Audience, Version(102, P2, ProtocolVersionStatuses.Published)).Should().BeFalse();
			ProtocolAccessRules.CanReadVersion(member, ById(P1), new List<ProtocolAudience>(), Version(102, P2, ProtocolVersionStatuses.Published)).Should().BeFalse();
		}

		#endregion g. Versions
	}
}
