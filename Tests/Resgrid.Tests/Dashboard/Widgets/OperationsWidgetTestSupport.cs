using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Builders shared by the operations and personnel widget provider tests.</summary>
	internal static class OperationsWidgetTestSupport
	{
		public const int Dept = 41;
		public const int OtherDept = 99;
		public const string Viewer = "viewer-1";

		public static readonly DateTime Now = new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);

		public static DashboardViewer ViewerWith(params string[] policies)
		{
			return new DashboardViewer
			{
				DepartmentId = Dept,
				UserId = Viewer,
				GrantedPolicies = new HashSet<string>(policies, StringComparer.Ordinal)
			};
		}

		/// <summary>A context with the settings validated and defaulted exactly as DashboardWidgetDataService does it.</summary>
		public static DashboardWidgetContext Context(string key, DashboardViewer viewer = null, IDictionary<string, object> settings = null, string grant = null)
		{
			var definition = DashboardWidgetCatalog.Find(key) ?? throw new InvalidOperationException($"{key} is not in the catalog");
			viewer ??= ViewerWith(definition.Gates.Policies.ToArray());

			return new DashboardWidgetContext
			{
				DepartmentId = viewer.DepartmentId,
				UserId = viewer.UserId,
				Viewer = viewer,
				Definition = definition,
				Settings = DashboardLayoutValidator.WithDefaults(definition, settings ?? new Dictionary<string, object>()),
				Size = definition.DefaultSize,
				NowUtc = Now,
				ProtectedGrantToken = grant
			};
		}

		public static Call Call(int id, int priority = 0, int department = Dept, DateTime? loggedOn = null, int state = 0, DateTime? dispatchOn = null,
			bool? dispatched = null, string reporter = "someone-else", string name = null)
		{
			return new Call
			{
				CallId = id,
				DepartmentId = department,
				Number = "C-" + id,
				Name = name ?? "Call " + id,
				Type = "Fire",
				Address = id + " Main St",
				Priority = priority,
				LoggedOn = loggedOn ?? Now.AddMinutes(-id),
				State = state,
				DispatchOn = dispatchOn,
				HasBeenDispatched = dispatched,
				ReportingUserId = reporter
			};
		}

		public static DepartmentGroup Group(int id, string name, int? parent = null, int type = (int)DepartmentGroupTypes.Orginizational, int department = Dept,
			params DepartmentGroupMember[] members)
		{
			return new DepartmentGroup
			{
				DepartmentGroupId = id,
				DepartmentId = department,
				Name = name,
				ParentDepartmentGroupId = parent,
				Type = type,
				Members = members.ToList()
			};
		}

		public static CustomState CustomStates(CustomStateTypes type, params CustomStateDetail[] details)
		{
			return new CustomState { CustomStateId = 500 + (int)type, DepartmentId = Dept, Type = (int)type, Details = details.ToList() };
		}

		public static CustomStateDetail Detail(int id, string text, string color, int baseType, string textColor = "#000000", int order = 0)
		{
			return new CustomStateDetail { CustomStateDetailId = id, ButtonText = text, ButtonColor = color, TextColor = textColor, BaseType = baseType, Order = order };
		}

		/// <summary>An ICustomStateService that answers with the department's custom states and the real built-in default lists.</summary>
		public static Mock<ICustomStateService> CustomStateService(params CustomState[] states)
		{
			var real = new Resgrid.Services.CustomStateService(null, null, null, null);
			var mock = new Mock<ICustomStateService>();
			mock.Setup(s => s.GetAllCustomStatesForDepartmentAsync(Dept)).ReturnsAsync(states.ToList());
			mock.Setup(s => s.GetDefaultUnitStatuses()).Returns(real.GetDefaultUnitStatuses);
			mock.Setup(s => s.GetDefaultPersonStatuses()).Returns(real.GetDefaultPersonStatuses);
			mock.Setup(s => s.GetDefaultPersonStaffings()).Returns(real.GetDefaultPersonStaffings);
			return mock;
		}

		/// <summary>A scope service for a department-wide viewer: every list comes back as it went in.</summary>
		public static Mock<IDispatchScopeService> DepartmentWideScope()
		{
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(s => s.GetScopeForUserAsync(Dept, Viewer)).ReturnsAsync(DispatchScope.DepartmentWide(Dept, Viewer, DispatchScopeReasons.ScopingDisabled));
			scope.Setup(s => s.FilterCallsForUserAsync(Dept, Viewer, It.IsAny<List<Call>>())).ReturnsAsync((int d, string u, List<Call> calls) => calls);
			scope.Setup(s => s.FilterCallsAsync(It.IsAny<DispatchScope>(), It.IsAny<List<Call>>())).ReturnsAsync((DispatchScope s, List<Call> calls) => calls);
			return scope;
		}

		/// <summary>A scope service that keeps only <paramref name="inScope"/> (a group-scoped dispatcher).</summary>
		public static Mock<IDispatchScopeService> ScopedTo(params int[] inScope)
		{
			var ids = new HashSet<int>(inScope);
			var narrow = new DispatchScope { DepartmentId = Dept, UserId = Viewer, IsDepartmentWide = false, AnchorGroupId = 1 };
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(s => s.GetScopeForUserAsync(Dept, Viewer)).ReturnsAsync(narrow);
			scope.Setup(s => s.FilterCallsForUserAsync(Dept, Viewer, It.IsAny<List<Call>>())).ReturnsAsync((int d, string u, List<Call> calls) => calls.Where(c => ids.Contains(c.CallId)).ToList());
			scope.Setup(s => s.FilterCallsAsync(It.IsAny<DispatchScope>(), It.IsAny<List<Call>>())).ReturnsAsync((DispatchScope s, List<Call> calls) => calls.Where(c => ids.Contains(c.CallId)).ToList());
			return scope;
		}

		public static Mock<ICallsService> Calls(List<Call> active = null, List<DepartmentCallPriority> priorities = null)
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(active ?? new List<Call>());
			calls.Setup(c => c.GetCallPrioritiesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(priorities ?? DefaultPriorities());
			calls.Setup(c => c.GetActiveCallDispatchCountsAsync(Dept, It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(new Dictionary<int, CallDispatchCounts>());
			calls.Setup(c => c.GetPendingCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call>());
			calls.Setup(c => c.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Call>());
			return calls;
		}

		public static List<DepartmentCallPriority> DefaultPriorities()
		{
			return new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 0, Name = "Low", Color = "#028602" },
				new DepartmentCallPriority { DepartmentCallPriorityId = 1, Name = "Medium", Color = "#DBDB2E" },
				new DepartmentCallPriority { DepartmentCallPriorityId = 2, Name = "High", Color = "#FFA500" },
				new DepartmentCallPriority { DepartmentCallPriorityId = 3, Name = "Emergency", Color = "#FF0000" }
			};
		}

		public static Unit Unit(int id, string name, int? station = null, string type = "Engine", int department = Dept, bool deleted = false)
		{
			return new Unit { UnitId = id, DepartmentId = department, Name = name, StationGroupId = station, Type = type, IsDeleted = deleted };
		}

		public static UnitState UnitState(Unit unit, int state, int unitStateId, DateTime? timestamp = null, string note = null)
		{
			return new UnitState { UnitStateId = unitStateId, UnitId = unit.UnitId, Unit = unit, State = state, Timestamp = timestamp ?? Now.AddMinutes(-5), Note = note };
		}

		/// <summary>Grants every unit (or person) view through the matrix except those listed.</summary>
		public static Mock<IAuthorizationService> Matrix(IEnumerable<int> hiddenUnits = null, IEnumerable<string> hiddenPeople = null)
		{
			var units = new HashSet<int>(hiddenUnits ?? Enumerable.Empty<int>());
			var people = new HashSet<string>(hiddenPeople ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.CanUserViewUnitViaMatrixAsync(It.IsAny<int>(), Viewer, Dept)).ReturnsAsync((int unitId, string u, int d) => !units.Contains(unitId));
			authorization.Setup(a => a.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), Viewer, Dept)).ReturnsAsync((string person, string u, int d) => !people.Contains(person));
			return authorization;
		}

		public static UserGroupRole Member(string userId, string first, string last, int? group = null)
		{
			return new UserGroupRole { UserId = userId, FirstName = first, LastName = last, DepartmentGroupId = group, RoleNames = "FF" };
		}
	}
}
