using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Security;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	[NonParallelizable]
	public class SecurityControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "audit-admin";
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IAuditService> _auditService;
		private SecurityController _controller;

		[SetUp]
		public void SetUp()
		{
			_departmentsService = new Mock<IDepartmentsService>();
			_auditService = new Mock<IAuditService>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(
						ResgridClaimTypes.Resources.Department,
						ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			_controller = new SecurityController(
				_departmentsService.Object,
				_auditService.Object,
				Mock.Of<IPermissionsService>(),
				Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<ISystemAuditsService>(),
				null,
				null,
				Mock.Of<IDepartmentSsoService>(),
				Mock.Of<IEncryptionService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task GetAuditLogsList_ProvidesChronologicalSortKeyAndDecisionColumns()
		{
			var older = new AuditLog
			{
				AuditLogId = 1,
				DepartmentId = DepartmentId,
				UserId = "actor-1",
				LoggedOn = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc),
				Successful = false,
				LogType = (int)AuditLogTypes.UserRemoved,
				Message = "Older"
			};
			var newer = new AuditLog
			{
				AuditLogId = 2,
				DepartmentId = DepartmentId,
				UserId = "actor-1",
				LoggedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				Successful = true,
				LogType = (int)AuditLogTypes.UserAdded,
				Message = "Newer"
			};
			var unknownTime = new AuditLog
			{
				AuditLogId = 3,
				DepartmentId = DepartmentId,
				LogType = (int)AuditLogTypes.PermissionsChanged,
				Message = "Unknown time"
			};

			_auditService.Setup(x => x.GetAllAuditLogsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<AuditLog> { older, newer, unknownTime });
			_auditService.Setup(x => x.GetAuditLogTypeString(It.IsAny<AuditLogTypes>()))
				.Returns((AuditLogTypes type) => type.ToString());
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false))
				.ReturnsAsync(new Department
				{
					DepartmentId = DepartmentId,
					TimeZone = "UTC",
					Use24HourTime = true
				});
			_departmentsService.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<PersonName>
				{
					new PersonName { UserId = "actor-1", FirstName = "Alex", LastName = "Morgan" }
				});
			_departmentsService.Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, true, false))
				.ReturnsAsync(new List<IdentityUser>
				{
					new IdentityUser
					{
						UserId = "actor-1",
						UserName = "alex.morgan",
						Email = "alex.morgan@example.com"
					}
				});

			var result = await _controller.GetAuditLogsList();

			var entries = result.Should().BeOfType<JsonResult>().Subject.Value
				.Should().BeAssignableTo<IEnumerable<AuditLogJson>>().Subject.ToList();
			entries.OrderByDescending(x => x.TimestampSort ?? -1).Select(x => x.AuditLogId)
				.Should().Equal(2, 1, 3);
			entries.Single(x => x.AuditLogId == 2).Name.Should().Be("Alex Morgan");
			entries.Single(x => x.AuditLogId == 2).Successful.Should().BeTrue();
			entries.Single(x => x.AuditLogId == 2).SearchTerms.Should().ContainAll(
				"Alex Morgan",
				"actor-1",
				"alex.morgan",
				"alex.morgan@example.com",
				"2",
				"2026-01-01 00:00:00",
				AuditLogTypes.UserAdded.ToString());
			entries.Single(x => x.AuditLogId == 3).TimestampSort.Should().BeNull();
		}

		[Test]
		public async Task ViewAudit_ReturnsCompleteAuditEntryAndFriendlyTypeName()
		{
			var auditLog = new AuditLog
			{
				AuditLogId = 42,
				DepartmentId = DepartmentId,
				UserId = "actor-1",
				LoggedOn = DateTime.UtcNow,
				Successful = true,
				LogType = (int)AuditLogTypes.UserAdded,
				Message = "User added",
				Data = "{\"userId\":\"new-user\"}",
				IpAddress = "192.0.2.10",
				ServerName = "web-1",
				ObjectId = "new-user",
				ObjectDepartmentId = DepartmentId,
				UserAgent = "Test Agent"
			};
			_auditService.Setup(x => x.GetAuditLogByIdAsync(42)).ReturnsAsync(auditLog);
			_auditService.Setup(x => x.GetAuditLogTypeString(AuditLogTypes.UserAdded))
				.Returns("User Added");
			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId });

			var result = await _controller.ViewAudit(42);

			var model = result.Should().BeOfType<ViewResult>().Subject.Model
				.Should().BeOfType<ViewAuditLogView>().Subject;
			model.AuditLog.Should().BeSameAs(auditLog);
			model.Type.Should().Be(AuditLogTypes.UserAdded);
			model.TypeName.Should().Be("User Added");
		}

		[Test]
		public async Task ViewAudit_MissingEntry_ReturnsNotFound()
		{
			_auditService.Setup(x => x.GetAuditLogByIdAsync(404)).ReturnsAsync((AuditLog)null);

			var result = await _controller.ViewAudit(404);

			result.Should().BeOfType<NotFoundResult>();
			_departmentsService.Verify(
				x => x.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()),
				Times.Never);
		}
	}
}
