using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed record ModuleImpactRequest(string ExpectedRevision, string Module, bool Disabled);
	public sealed record ModuleImpactCounts(int Members, int? ContentRows);
	public interface IModuleImpactStore
	{
		Task<ModuleImpactCounts> ReadModuleImpactCountsAsync(int departmentId, string module, int bound, CancellationToken cancellationToken);
	}
	public interface IModuleImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, ModuleImpactRequest request, CancellationToken cancellationToken = default);
	}
	/// <summary>Legacy menu switches without an additional per-member claim gate in _Navigation or its mailbox entry.</summary>
	public static class ModuleImpactSelection
	{
		public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(new[] {
			"Messaging", "Mapping", "Shifts", "Logs", "Reports", "Documents", "Calendar", "Notes", "Training", "Inventory" });
		public static bool Disabled(DepartmentModuleSettings value, string module) => module switch {
			"Messaging" => value.MessagingDisabled, "Mapping" => value.MappingDisabled, "Shifts" => value.ShiftsDisabled,
			"Logs" => value.LogsDisabled, "Reports" => value.ReportsDisabled, "Documents" => value.DocumentsDisabled,
			"Calendar" => value.CalendarDisabled, "Notes" => value.NotesDisabled, "Training" => value.TrainingDisabled,
			"Inventory" => value.InventoryDisabled, _ => throw new ArgumentException("Unsupported module preview.") };
	}
}
