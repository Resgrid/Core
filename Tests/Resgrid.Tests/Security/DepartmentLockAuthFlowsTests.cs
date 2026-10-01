using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// A department operation lock (ADP plan section 20.2) pauses data entry, never authentication or session flows (workbook
	/// section 12, slice 25): a locked shared session must still unlock or end its shift, Responder must still approve, and a
	/// member must still sign out, verify a second factor and manage their own sign-in methods.
	/// </summary>
	[TestFixture]
	public class DepartmentLockAuthFlowsTests
	{
		private static ActionExecutingContext Context(Type controller, string action)
		{
			var method = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).First(m => m.Name == action);
			var descriptor = new ControllerActionDescriptor
			{
				ControllerTypeInfo = controller.GetTypeInfo(),
				MethodInfo = method,
				ActionName = action,
				ControllerName = controller.Name,
				EndpointMetadata = controller.GetCustomAttributes(true).Concat(method.GetCustomAttributes(true)).ToList()
			};

			var locks = new Mock<IDepartmentLockService>();
			locks.Setup(l => l.IsDepartmentLockedAsync(42)).ReturnsAsync(true);
			locks.Setup(l => l.GetActiveLockAsync(42, It.IsAny<bool>())).ReturnsAsync(new DepartmentOperationLock { DepartmentId = 42, Reason = "migration" });
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimaryGroupSid, "42") }, "test")),
				RequestServices = new ServiceCollection().AddSingleton(locks.Object).BuildServiceProvider()
			};
			http.Request.Method = "POST";
			return new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor), new List<IFilterMetadata>(), new Dictionary<string, object>(), null);
		}

		private static async Task<bool> Passes(IAsyncActionFilter filter, Type controller, string action)
		{
			var context = Context(controller, action);
			var reached = false;
			await filter.OnActionExecutionAsync(context, () =>
			{
				reached = true;
				return Task.FromResult<ActionExecutedContext>(null);
			});
			return reached && context.Result == null;
		}

		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.SessionsController), "Lock")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.SessionsController), "EndShift")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.SessionsController), "CompleteUnlock")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.SessionsController), "RequestUnlockApproval")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.SessionsController), "BeginUnlockSso")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.MfaApprovalController), "Approve")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.MfaApprovalController), "Deny")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.MfaController), "VerifyStepUp")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.PasskeysController), "Revoke")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.AccountSecurityController), "ReportActivity")]
		[TestCase(typeof(Resgrid.Web.Services.Controllers.v4.SsoController), "Begin")]
		public async Task Api_authentication_and_session_flows_continue_during_a_department_lock(Type controller, string action)
		{
			(await Passes(new Resgrid.Web.Services.Filters.DepartmentLockActionFilter(), controller, action)).Should().BeTrue();
		}

		[TestCase(typeof(Resgrid.Web.Controllers.SharedSessionController), "Lock")]
		[TestCase(typeof(Resgrid.Web.Controllers.SharedSessionController), "EndShift")]
		[TestCase(typeof(Resgrid.Web.Controllers.SharedSessionController), "Unlock")]
		[TestCase(typeof(Resgrid.Web.Controllers.AccountController), "LogOff")]
		[TestCase(typeof(Resgrid.Web.Controllers.AccountController), "SsoUnlockBegin")]
		[TestCase(typeof(Resgrid.Web.Areas.User.Controllers.TwoFactorController), "Verify2FA")]
		[TestCase(typeof(Resgrid.Web.Areas.User.Controllers.AccountSecurityController), "Reauthenticate")]
		[TestCase(typeof(Resgrid.Web.Areas.User.Controllers.PasskeysController), "Remove")]
		public async Task Web_authentication_and_session_flows_continue_during_a_department_lock(Type controller, string action)
		{
			(await Passes(new Resgrid.Web.Filters.DepartmentLockActionFilter(), controller, action)).Should().BeTrue();
		}

		[Test]
		public async Task Data_entry_still_waits_for_the_lock()
		{
			(await Passes(new Resgrid.Web.Services.Filters.DepartmentLockActionFilter(), typeof(Resgrid.Web.Services.Controllers.v4.CallsController), "SaveCall"))
				.Should().BeFalse();
			(await Passes(new Resgrid.Web.Filters.DepartmentLockActionFilter(), typeof(Resgrid.Web.Areas.User.Controllers.DispatchController), "NewCall"))
				.Should().BeFalse();
		}
	}
}
