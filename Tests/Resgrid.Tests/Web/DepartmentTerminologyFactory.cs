using Microsoft.AspNetCore.Http;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>Builds the request-bound <see cref="DepartmentTerminology"/> for controller tests.</summary>
	public static class DepartmentTerminologyFactory
	{
		/// <summary>
		/// Terminology for a request: <paramref name="config"/> is what setting 118 returns (null = nothing saved); localizers are
		/// loose mocks, so shipped names fall back to the English catalog names.
		/// </summary>
		public static DepartmentTerminology Create(DepartmentTerminologyConfig config = null, int departmentId = 1)
		{
			return CreateForRequest(config, departmentId).Terms;
		}

		/// <summary>As <see cref="Create"/>, with the request it reads from (where <see cref="DepartmentTerminology.PrepareAsync"/> stores the substitution).</summary>
		public static (DepartmentTerminology Terms, HttpContext Context, IHttpContextAccessor Accessor) CreateForRequest(
			DepartmentTerminologyConfig config = null, int departmentId = 1)
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentTerminologyAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(config ?? new DepartmentTerminologyConfig());

			var context = new DefaultHttpContext();
			context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
			{
				new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.PrimaryGroupSid, departmentId.ToString())
			}, "test"));
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.SetupGet(a => a.HttpContext).Returns(context);

			var terms = new DepartmentTerminology(settings.Object, accessor.Object, new AdminAssistFieldHelpTestServices.ShippedLocalizers(null));
			return (terms, context, accessor.Object);
		}
	}
}
