using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Checklists
{
	public enum ChecklistAssignmentType { Automatic = 0, User = 1, Role = 2, Group = 3, Unit = 4 }
	public sealed class ChecklistAssignmentChoice { public int Type { get; set; } public string Id { get; set; } public string Name { get; set; } }
	public sealed class ChecklistAssetTarget
	{
		public int DepartmentId { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }
		public int? UnitId { get; set; }
		public int? GroupId { get; set; }
		public string UserId { get; set; }
	}
	/// <summary>Inventory integration boundary. Attended names honor the actor/grant; routing returns identifiers only.
	/// Inventory's serialized-asset module replaces the unavailable implementation when installed.</summary>
	public interface IChecklistAssetSource
	{
		Task<bool> IsAvailableAsync(int departmentId);
		Task<List<ChecklistAssetTarget>> ListAsync(ChecklistActor actor);
		Task<ChecklistAssetTarget> GetAsync(ChecklistActor actor, string id);
		Task<ChecklistAssetTarget> RoutingAsync(int departmentId, string id);
		/// <summary>Grant-free authorization for a generic notice; must not reveal asset names or content.</summary>
		Task<bool> CanReceiveReminderAsync(int departmentId, string userId, string id);
	}
	public interface IChecklistAssignmentService
	{
		Task ValidateAsync(int departmentId, int type, string id);
		Task<List<ChecklistAssignmentChoice>> ChoicesAsync(ChecklistActor actor);
		Task<HashSet<string>> MembersAsync(int departmentId, int type, string id);
		Task<bool> CanPerformAsync(ChecklistActor actor, ChecklistSchedule schedule);
	}
	public sealed class ChecklistCalendarEntry
	{
		public string Id { get; set; }
		public string OccurrenceId { get; set; }
		public string Title { get; set; }
		public DateTime StartUtc { get; set; }
		public DateTime EndUtc { get; set; }
		public int State { get; set; }
		public bool IsRedacted { get; set; }
	}
	public sealed class ChecklistShiftStart { public string WorkshiftDayId { get; set; } public DateTime StartUtc { get; set; } public int UnitId { get; set; } }
}
