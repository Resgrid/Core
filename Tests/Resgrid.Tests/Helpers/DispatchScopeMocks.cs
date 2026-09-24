using System.Collections.Generic;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Helpers
{
	/// <summary>Stand-ins for <see cref="IDispatchScopeService"/> in tests that aren't about dispatch scope.</summary>
	public static class DispatchScopeMocks
	{
		/// <summary>Group-scoped dispatch off: every user is department-wide and every call is in scope.</summary>
		public static IDispatchScopeService Off()
		{
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(x => x.GetScopeForUserAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int departmentId, string userId) => DispatchScope.DepartmentWide(departmentId, userId, DispatchScopeReasons.ScopingDisabled));
			scope.Setup(x => x.IsCallInScopeAsync(It.IsAny<DispatchScope>(), It.IsAny<Call>())).ReturnsAsync(true);
			scope.Setup(x => x.CanUserAccessCallAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Call>())).ReturnsAsync(true);
			scope.Setup(x => x.FilterCallsAsync(It.IsAny<DispatchScope>(), It.IsAny<List<Call>>()))
				.ReturnsAsync((DispatchScope s, List<Call> calls) => calls);
			scope.Setup(x => x.FilterCallsForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<List<Call>>()))
				.ReturnsAsync((int departmentId, string userId, List<Call> calls) => calls);
			return scope.Object;
		}
	}
}
