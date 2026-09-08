using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Inspection programs, adopted code sets, inspections, violations and re-inspection (RMS plan section 4.3, RMS-5).
	/// A completed inspection turns every failed checklist item into an Open violation citing the item's code section;
	/// the daily sweep raises trigger 162 once per violation as it passes its due date.
	/// </summary>
	public class RecordsInspectionsService : IRecordsInspectionsService
	{
		public const string InspectionAggregate = "RmsInspection";
		public const string ViolationAggregate = "RmsViolation";
		private const int SweepBatch = 200;

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsCodeSetsRepository _codeSets;
		private readonly IRmsCodeSectionsRepository _codeSections;
		private readonly IRmsInspectionProgramsRepository _programs;
		private readonly IRmsInspectionsRepository _inspections;
		private readonly IRmsViolationsRepository _violations;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IRmsPreventionAttachmentsRepository _attachments;
		private readonly IRecordsProtectionService _protection;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsInspectionsService(RecordsPreventionGate gate, IRmsCodeSetsRepository codeSets, IRmsCodeSectionsRepository codeSections, IRmsInspectionProgramsRepository programs,
			IRmsInspectionsRepository inspections, IRmsViolationsRepository violations, IRmsOccupanciesRepository occupancies, IRmsPreventionAttachmentsRepository attachments,
			IRecordsProtectionService protection, IDomainEventOutboxService outbox, IUnitOfWork unitOfWork)
		{
			_gate = gate; _codeSets = codeSets; _codeSections = codeSections; _programs = programs; _inspections = inspections; _violations = violations;
			_occupancies = occupancies; _attachments = attachments; _protection = protection; _outbox = outbox; _unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Inspections);

		private Task RequireViewAsync(int departmentId, string userId) => Task.WhenAll(_gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Inspections), _gate.RequireViewerAsync(departmentId, userId));

		private async Task RequireAdminAsync(int departmentId, string userId)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Inspections);
			await _gate.RequireAdminAsync(departmentId, userId);
		}

		#region Code sets

		public async Task<List<RmsCodeSet>> GetCodeSetsAsync(int departmentId, string userId, bool includeInactive)
		{
			await RequireViewAsync(departmentId, userId);
			return (await _codeSets.GetForDepartmentAsync(departmentId, includeInactive))?.ToList() ?? new List<RmsCodeSet>();
		}

		public async Task<RmsCodeSet> SaveCodeSetAsync(int departmentId, string userId, RmsCodeSet input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsCodeSetId) ? null : await _codeSets.GetByIdForDepartmentAsync(departmentId, input.RmsCodeSetId);
			var isNew = entity == null;
			if (isNew) entity = new RmsCodeSet { RmsCodeSetId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 1 };
			else entity.RowVersion++;
			entity.Name = RecordsPreventionGate.Require(input.Name, 200, "A code set needs a name.");
			entity.Edition = RecordsPreventionGate.Trim(input.Edition, 100); entity.Jurisdiction = RecordsPreventionGate.Trim(input.Jurisdiction, 200);
			entity.IsActive = input.IsActive; entity.ModifiedOn = now;
			if (isNew) await _codeSets.InsertAsync(entity, cancellationToken, true); else await _codeSets.UpdateAsync(entity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, isNew ? "Code set created" : "Code set updated", entity.RmsCodeSetId, new { entity.Name, entity.Edition }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task<List<RmsCodeSection>> GetCodeSectionsAsync(int departmentId, string userId, string codeSetId)
		{
			await RequireViewAsync(departmentId, userId);
			return (await _codeSections.GetForCodeSetAsync(departmentId, codeSetId))?.ToList() ?? new List<RmsCodeSection>();
		}

		public async Task<RmsCodeSection> SaveCodeSectionAsync(int departmentId, string userId, RmsCodeSection input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var set = await _codeSets.GetByIdForDepartmentAsync(departmentId, input.RmsCodeSetId);
			if (set == null || set.DeletedOn != null) throw new ArgumentException("The code set does not exist.");
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsCodeSectionId) ? null : await _codeSections.GetByIdForDepartmentAsync(departmentId, input.RmsCodeSectionId);
			var isNew = entity == null;
			if (isNew) entity = new RmsCodeSection { RmsCodeSectionId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsCodeSetId = set.RmsCodeSetId, CreatedOn = now, RowVersion = 1 };
			else entity.RowVersion++;
			entity.SectionNumber = RecordsPreventionGate.Require(input.SectionNumber, 64, "A code section needs a section number.");
			entity.Title = RecordsPreventionGate.Trim(input.Title, 300); entity.Text = RecordsPreventionGate.Trim(input.Text, 8000);
			entity.DefaultSeverity = Math.Clamp(input.DefaultSeverity == 0 ? (int)RmsViolationSeverity.Moderate : input.DefaultSeverity, 1, 4);
			entity.DefaultCorrectionDays = Math.Clamp(input.DefaultCorrectionDays <= 0 ? 30 : input.DefaultCorrectionDays, 1, 365);
			entity.ModifiedOn = now;
			if (isNew) await _codeSections.InsertAsync(entity, cancellationToken, true); else await _codeSections.UpdateAsync(entity, cancellationToken, true);
			return entity;
		}

		public async Task<int> ImportCodeSectionsAsync(int departmentId, string userId, string codeSetId, string csv, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var set = await _codeSets.GetByIdForDepartmentAsync(departmentId, codeSetId);
			if (set == null || set.DeletedOn != null) throw new ArgumentException("The code set does not exist.");
			if (string.IsNullOrWhiteSpace(csv)) throw new ArgumentException("The import file is empty.");
			var existing = ((await _codeSections.GetForCodeSetAsync(departmentId, codeSetId)) ?? Enumerable.Empty<RmsCodeSection>()).Select(s => s.SectionNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var created = 0; var now = DateTime.UtcNow;
			foreach (var line in csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)))
			{
				var cells = CsvSplit(line);
				if (cells.Count == 0 || cells[0].Equals("section", StringComparison.OrdinalIgnoreCase)) continue;
				var number = cells[0].Trim();
				if (number.Length == 0 || number.Length > 64 || existing.Contains(number)) continue;
				int.TryParse(cells.Count > 3 ? cells[3] : "", out var severity);
				int.TryParse(cells.Count > 4 ? cells[4] : "", out var days);
				await _codeSections.InsertAsync(new RmsCodeSection
				{
					RmsCodeSectionId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsCodeSetId = codeSetId, SectionNumber = number,
					Title = RecordsPreventionGate.Trim(cells.Count > 1 ? cells[1] : null, 300), Text = RecordsPreventionGate.Trim(cells.Count > 2 ? cells[2] : null, 8000),
					DefaultSeverity = Math.Clamp(severity == 0 ? 2 : severity, 1, 4), DefaultCorrectionDays = Math.Clamp(days <= 0 ? 30 : days, 1, 365), CreatedOn = now, ModifiedOn = now, RowVersion = 1
				}, cancellationToken, true);
				existing.Add(number); created++;
				if (created >= 5000) break;
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Code sections imported", codeSetId, new { created }, cancellationToken: cancellationToken);
			return created;
		}

		public static List<string> CsvSplit(string line)
		{
			var cells = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false;
			for (var i = 0; i < line.Length; i++)
			{
				var c = line[i];
				if (quoted)
				{
					if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
					else if (c == '"') quoted = false;
					else current.Append(c);
				}
				else if (c == '"') quoted = true;
				else if (c == ',') { cells.Add(current.ToString()); current.Clear(); }
				else current.Append(c);
			}
			cells.Add(current.ToString());
			return cells;
		}

		#endregion

		#region Programs

		public async Task<List<RmsInspectionProgram>> GetProgramsAsync(int departmentId, string userId, bool includeInactive)
		{
			await RequireViewAsync(departmentId, userId);
			return (await _programs.GetForDepartmentAsync(departmentId, includeInactive))?.ToList() ?? new List<RmsInspectionProgram>();
		}

		public async Task<RmsInspectionProgram> GetProgramAsync(int departmentId, string userId, string programId)
		{
			await RequireViewAsync(departmentId, userId);
			var program = await _programs.GetByIdForDepartmentAsync(departmentId, programId);
			return program == null || program.DeletedOn != null ? null : program;
		}

		public async Task<RmsInspectionProgram> SaveProgramAsync(int departmentId, string userId, RmsInspectionProgram input, List<RmsInspectionChecklistItem> checklist, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsInspectionProgramId) ? null : await _programs.GetByIdForDepartmentAsync(departmentId, input.RmsInspectionProgramId);
			var isNew = entity == null;
			if (isNew) entity = new RmsInspectionProgram { RmsInspectionProgramId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 1 };
			else entity.RowVersion++;
			entity.Name = RecordsPreventionGate.Require(input.Name, 200, "An inspection program needs a name.");
			entity.Description = RecordsPreventionGate.Trim(input.Description, 4000);
			entity.OccupancyTypesCsv = RecordsPreventionGate.Trim(input.OccupancyTypesCsv, 500);
			entity.FrequencyMonths = Math.Clamp(input.FrequencyMonths, 0, 120);
			entity.RmsCodeSetId = RecordsPreventionGate.Trim(input.RmsCodeSetId, 36);
			entity.IsActive = input.IsActive; entity.ModifiedOn = now;
			var items = (checklist ?? new List<RmsInspectionChecklistItem>()).Where(i => i != null && !string.IsNullOrWhiteSpace(i.Text)).Select((i, index) => new RmsInspectionChecklistItem
			{ Key = string.IsNullOrWhiteSpace(i.Key) ? $"item{index + 1}" : i.Key.Trim(), Text = i.Text.Trim(), RmsCodeSectionId = RecordsPreventionGate.Trim(i.RmsCodeSectionId, 36), Required = i.Required, Order = index }).ToList();
			if (items.Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count) throw new ArgumentException("Checklist item keys must be unique.");
			if (items.Count > 300) throw new ArgumentException("A checklist holds at most 300 items.");
			entity.ChecklistJson = JsonConvert.SerializeObject(items);
			if (isNew) await _programs.InsertAsync(entity, cancellationToken, true); else await _programs.UpdateAsync(entity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, isNew ? "Inspection program created" : "Inspection program updated", entity.RmsInspectionProgramId, new { entity.Name, items = items.Count, entity.FrequencyMonths }, cancellationToken: cancellationToken);
			return entity;
		}

		public static List<RmsInspectionChecklistItem> ParseChecklist(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsInspectionChecklistItem>();
			try { return JsonConvert.DeserializeObject<List<RmsInspectionChecklistItem>>(json) ?? new List<RmsInspectionChecklistItem>(); }
			catch (JsonException) { return new List<RmsInspectionChecklistItem>(); }
		}

		public static List<RmsInspectionItemResult> ParseItems(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsInspectionItemResult>();
			try { return JsonConvert.DeserializeObject<List<RmsInspectionItemResult>>(json) ?? new List<RmsInspectionItemResult>(); }
			catch (JsonException) { return new List<RmsInspectionItemResult>(); }
		}

		#endregion

		#region Inspections

		public async Task<List<RmsInspection>> ListAsync(int departmentId, string userId, RmsInspectionQuery query)
		{
			await RequireViewAsync(departmentId, userId);
			var rows = (await _inspections.QueryAsync(departmentId, query ?? new RmsInspectionQuery()))?.ToList() ?? new List<RmsInspection>();
			await _protection.RevealInspectionsAsync(departmentId, rows);
			return rows;
		}

		public async Task<int> CountAsync(int departmentId, string userId, RmsInspectionQuery query)
		{
			await RequireViewAsync(departmentId, userId);
			return await _inspections.CountAsync(departmentId, query ?? new RmsInspectionQuery());
		}

		public async Task<InspectionAggregate> GetAsync(int departmentId, string userId, string inspectionId)
		{
			await RequireViewAsync(departmentId, userId);
			var inspection = await LiveAsync(departmentId, inspectionId);
			if (inspection == null) return null;
			var aggregate = new InspectionAggregate
			{
				Inspection = inspection,
				Program = string.IsNullOrWhiteSpace(inspection.RmsInspectionProgramId) ? null : await _programs.GetByIdForDepartmentAsync(departmentId, inspection.RmsInspectionProgramId),
				Occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, inspection.RmsOccupancyId),
				Items = ParseItems(inspection.ItemsJson),
				Violations = (await _violations.GetForInspectionAsync(departmentId, inspectionId))?.ToList() ?? new List<RmsViolation>(),
				Attachments = (await _attachments.GetMetadataForParentAsync(departmentId, RmsPreventionParentKind.Inspection, inspectionId))?.ToList() ?? new List<RmsPreventionAttachment>()
			};
			aggregate.Checklist = ParseChecklist(aggregate.Program?.ChecklistJson);
			aggregate.Protection.Merge(await _protection.RevealInspectionsAsync(departmentId, new[] { inspection }));
			aggregate.Protection.Merge(await _protection.RevealViolationsAsync(departmentId, aggregate.Violations));
			aggregate.Protection.Merge(await _protection.RevealPreventionAttachmentsAsync(departmentId, aggregate.Attachments, false));
			return aggregate;
		}

		public async Task<RmsInspection> ScheduleAsync(int departmentId, string userId, string occupancyId, string programId, DateTime scheduledOn, string inspectorUserId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, occupancyId);
			if (occupancy == null || occupancy.DeletedOn != null) throw new ArgumentException("The occupancy does not exist.");
			RmsInspectionProgram program = null;
			if (!string.IsNullOrWhiteSpace(programId))
			{
				program = await _programs.GetByIdForDepartmentAsync(departmentId, programId);
				if (program == null || program.DeletedOn != null) throw new ArgumentException("The inspection program does not exist.");
			}
			var inspection = await CreateScheduledAsync(departmentId, userId, occupancy, program, scheduledOn, inspectorUserId, null, cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Inspection scheduled", inspection.RmsInspectionId, new { inspection.InspectionNumber, occupancyId, programId, scheduledOn }, cancellationToken: cancellationToken);
			return inspection;
		}

		private async Task<RmsInspection> CreateScheduledAsync(int departmentId, string userId, RmsOccupancy occupancy, RmsInspectionProgram program, DateTime scheduledOn, string inspectorUserId, string parentInspectionId, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var inspection = new RmsInspection
			{
				RmsInspectionId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = occupancy.RmsOccupancyId, RmsInspectionProgramId = program?.RmsInspectionProgramId,
				InspectionNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.Inspection, now, cancellationToken), State = (int)RmsInspectionState.Scheduled, Result = (int)RmsInspectionResult.NotRecorded,
				ScheduledOn = scheduledOn == default ? now : scheduledOn, InspectorUserId = RecordsPreventionGate.Trim(inspectorUserId, 128), ParentInspectionId = parentInspectionId,
				CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1
			};
			await _inspections.InsertAsync(inspection, cancellationToken, true);
			return inspection;
		}

		public async Task<RmsInspection> StartAsync(int departmentId, string userId, string inspectionId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var inspection = await LiveAsync(departmentId, inspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			if (inspection.State != (int)RmsInspectionState.Scheduled) throw new InvalidOperationException("Only a scheduled inspection can be started.");
			inspection.State = (int)RmsInspectionState.InProgress; inspection.StartedOn = DateTime.UtcNow; inspection.InspectorUserId ??= userId; inspection.ModifiedOn = inspection.StartedOn.Value; inspection.RowVersion++;
			await _inspections.UpdateAsync(inspection, cancellationToken, true);
			return inspection;
		}

		public async Task<InspectionAggregate> CompleteAsync(int departmentId, string userId, string inspectionId, List<RmsInspectionItemResult> items, string notes, string signatureName, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var inspection = await LiveAsync(departmentId, inspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			if (inspection.State != (int)RmsInspectionState.Scheduled && inspection.State != (int)RmsInspectionState.InProgress) throw new InvalidOperationException("The inspection is not open.");
			var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, inspection.RmsOccupancyId) ?? throw new InvalidOperationException("The occupancy no longer exists.");
			var program = string.IsNullOrWhiteSpace(inspection.RmsInspectionProgramId) ? null : await _programs.GetByIdForDepartmentAsync(departmentId, inspection.RmsInspectionProgramId);
			var checklist = ParseChecklist(program?.ChecklistJson);
			items = (items ?? new List<RmsInspectionItemResult>()).Where(i => i != null && !string.IsNullOrWhiteSpace(i.Key)).ToList();
			var missingRequired = checklist.Where(c => c.Required && !items.Any(i => i.Key == c.Key && i.Passed.HasValue)).Select(c => c.Key).ToList();
			if (missingRequired.Count > 0) throw new ArgumentException($"Required checklist items were not inspected: {string.Join(", ", missingRequired)}.");

			var now = DateTime.UtcNow;
			var failed = items.Where(i => i.Passed == false).ToList();
			var failedRequired = failed.Any(f => checklist.Any(c => c.Key == f.Key && c.Required));
			var sectionIds = checklist.Where(c => failed.Any(f => f.Key == c.Key) && !string.IsNullOrWhiteSpace(c.RmsCodeSectionId)).Select(c => c.RmsCodeSectionId).Distinct().ToList();
			var sections = sectionIds.Count == 0 ? new Dictionary<string, RmsCodeSection>() : ((await _codeSections.GetByIdsAsync(departmentId, sectionIds)) ?? Enumerable.Empty<RmsCodeSection>()).ToDictionary(s => s.RmsCodeSectionId);

			inspection.ItemsJson = JsonConvert.SerializeObject(items);
			inspection.Notes = RecordsPreventionGate.Trim(notes, 8000);
			inspection.SignatureName = RecordsPreventionGate.Trim(signatureName, 200);
			inspection.SignedOn = inspection.SignatureName == null ? null : now;
			inspection.CompletedOn = now; inspection.StartedOn ??= now; inspection.InspectorUserId ??= userId;
			inspection.Result = failed.Count == 0 ? (int)RmsInspectionResult.Pass : failedRequired ? (int)RmsInspectionResult.Fail : (int)RmsInspectionResult.Conditional;
			inspection.State = failed.Count == 0 ? (int)RmsInspectionState.Completed : (int)RmsInspectionState.ReinspectionRequired;
			inspection.ModifiedOn = now; inspection.RowVersion++;

			var violations = new List<RmsViolation>();
			foreach (var item in failed)
			{
				var definition = checklist.FirstOrDefault(c => c.Key == item.Key);
				RmsCodeSection section = definition?.RmsCodeSectionId != null && sections.TryGetValue(definition.RmsCodeSectionId, out var s) ? s : null;
				violations.Add(new RmsViolation
				{
					RmsViolationId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInspectionId = inspection.RmsInspectionId, RmsOccupancyId = occupancy.RmsOccupancyId,
					RmsCodeSetId = section?.RmsCodeSetId ?? program?.RmsCodeSetId, RmsCodeSectionId = section?.RmsCodeSectionId, ChecklistItemKey = item.Key,
					Description = RecordsPreventionGate.Trim(string.IsNullOrWhiteSpace(item.Note) ? definition?.Text : item.Note, 4000) ?? "Checklist item failed",
					Severity = section?.DefaultSeverity ?? (int)RmsViolationSeverity.Moderate, CorrectiveAction = section?.Title == null ? null : $"Correct per {section.SectionNumber} {section.Title}",
					DueOn = now.AddDays(section?.DefaultCorrectionDays ?? 30), State = (int)RmsViolationState.Open, CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1
				});
			}

			var inspectionPlain = PlaintextSnapshot<RmsInspection>.Take(inspection, RmsProtectedFields.Inspections);
			var violationPlain = violations.Select(v => PlaintextSnapshot<RmsViolation>.Take(v, RmsProtectedFields.Violations)).ToList();
			long outboxId;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectInspectionAsync(departmentId, inspection, null, userId, cancellationToken);
				await _inspections.UpdateAsync(inspection, cancellationToken, true);
				foreach (var violation in violations)
				{
					await _protection.ProtectViolationAsync(departmentId, violation, null, userId, cancellationToken);
					await _violations.InsertAsync(violation, cancellationToken, true);
				}
				occupancy.LastInspectedOn = now; occupancy.ModifiedOn = now; occupancy.RowVersion++;
				await _occupancies.UpdateAsync(occupancy, cancellationToken, true);
				outboxId = await EnqueueInspectionAsync(inspection, occupancy, program, violations.Count, violations.Count(v => v.Severity == (int)RmsViolationSeverity.Critical), cancellationToken);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Inspection completed", inspection.RmsInspectionId, new { inspection.InspectionNumber, result = inspection.Result, violations = violations.Count }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			inspectionPlain.Restore();
			foreach (var p in violationPlain) p.Restore();
			await _outbox.DispatchAfterCommitAsync(new[] { outboxId }, cancellationToken);

			return new InspectionAggregate { Inspection = inspection, Program = program, Occupancy = occupancy, Checklist = checklist, Items = items, Violations = violations };
		}

		public async Task<RmsInspection> ScheduleReinspectionAsync(int departmentId, string userId, string inspectionId, DateTime scheduledOn, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var parent = await LiveAsync(departmentId, inspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			if (parent.State != (int)RmsInspectionState.ReinspectionRequired && parent.State != (int)RmsInspectionState.Completed) throw new InvalidOperationException("Only a completed inspection can be re-inspected.");
			var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, parent.RmsOccupancyId) ?? throw new InvalidOperationException("The occupancy no longer exists.");
			var program = string.IsNullOrWhiteSpace(parent.RmsInspectionProgramId) ? null : await _programs.GetByIdForDepartmentAsync(departmentId, parent.RmsInspectionProgramId);
			var child = await CreateScheduledAsync(departmentId, userId, occupancy, program, scheduledOn, parent.InspectorUserId, parent.RmsInspectionId, cancellationToken);
			foreach (var violation in ((await _violations.GetForInspectionAsync(departmentId, inspectionId)) ?? Enumerable.Empty<RmsViolation>()).Where(v => v.IsOpen))
			{ violation.ReinspectionId = child.RmsInspectionId; violation.ModifiedOn = DateTime.UtcNow; violation.RowVersion++; await _violations.UpdateAsync(violation, cancellationToken, true); }
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Re-inspection scheduled", child.RmsInspectionId, new { parent = parent.RmsInspectionId, scheduledOn }, cancellationToken: cancellationToken);
			return child;
		}

		public async Task<RmsInspection> CloseAsync(int departmentId, string userId, string inspectionId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var inspection = await LiveAsync(departmentId, inspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			if (inspection.State != (int)RmsInspectionState.Completed && inspection.State != (int)RmsInspectionState.ReinspectionRequired) throw new InvalidOperationException("Only a completed inspection can be closed.");
			var open = ((await _violations.GetForInspectionAsync(departmentId, inspectionId)) ?? Enumerable.Empty<RmsViolation>()).Count(v => v.IsOpen || v.State == (int)RmsViolationState.Corrected);
			if (open > 0) throw new InvalidOperationException($"{open} violation(s) are not yet verified or waived.");
			inspection.State = (int)RmsInspectionState.Closed; inspection.ModifiedOn = DateTime.UtcNow; inspection.RowVersion++;
			await _inspections.UpdateAsync(inspection, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Inspection closed", inspectionId, null, cancellationToken: cancellationToken);
			return inspection;
		}

		public async Task<RmsInspection> CancelAsync(int departmentId, string userId, string inspectionId, string reason, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var inspection = await LiveAsync(departmentId, inspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			if (inspection.State != (int)RmsInspectionState.Scheduled && inspection.State != (int)RmsInspectionState.InProgress) throw new InvalidOperationException("Only an open inspection can be cancelled.");
			inspection.State = (int)RmsInspectionState.Cancelled; inspection.ModifiedOn = DateTime.UtcNow; inspection.RowVersion++;
			await _inspections.UpdateAsync(inspection, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Inspection cancelled", inspectionId, new { reason = RecordsPreventionGate.Trim(reason, 500) }, cancellationToken: cancellationToken);
			return inspection;
		}

		public async Task<RmsInspection> IssueNoticeAsync(int departmentId, string userId, string inspectionId, string noticeReference, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var inspection = await LiveAsync(departmentId, inspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			if (inspection.CompletedOn == null) throw new InvalidOperationException("A notice follows a completed inspection.");
			inspection.NoticeIssuedOn = DateTime.UtcNow; inspection.NoticeReference = RecordsPreventionGate.Trim(noticeReference, 100); inspection.ModifiedOn = inspection.NoticeIssuedOn.Value; inspection.RowVersion++;
			await _inspections.UpdateAsync(inspection, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Notice of violation issued", inspectionId, new { inspection.NoticeReference }, cancellationToken: cancellationToken);
			return inspection;
		}

		private async Task<RmsInspection> LiveAsync(int departmentId, string inspectionId)
		{
			if (string.IsNullOrWhiteSpace(inspectionId)) return null;
			var inspection = await _inspections.GetByIdForDepartmentAsync(departmentId, inspectionId);
			return inspection == null || inspection.DeletedOn != null ? null : inspection;
		}

		#endregion

		#region Violations

		public async Task<List<RmsViolation>> GetViolationsForOccupancyAsync(int departmentId, string userId, string occupancyId, bool openOnly)
		{
			await RequireViewAsync(departmentId, userId);
			var rows = (await _violations.GetForOccupancyAsync(departmentId, occupancyId, openOnly))?.ToList() ?? new List<RmsViolation>();
			await _protection.RevealViolationsAsync(departmentId, rows);
			return rows;
		}

		public async Task<List<RmsViolation>> GetOpenViolationsAsync(int departmentId, string userId, int take)
		{
			await RequireViewAsync(departmentId, userId);
			var rows = (await _violations.GetOpenAsync(departmentId, take))?.ToList() ?? new List<RmsViolation>();
			await _protection.RevealViolationsAsync(departmentId, rows);
			return rows;
		}

		public async Task<RmsViolation> SaveViolationAsync(int departmentId, string userId, RmsViolation input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var inspection = await LiveAsync(departmentId, input.RmsInspectionId) ?? throw new ArgumentException("The inspection does not exist.");
			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(input.RmsViolationId) ? null : await _violations.GetByIdForDepartmentAsync(departmentId, input.RmsViolationId);
			var entity = existing ?? new RmsViolation { RmsViolationId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsInspectionId = inspection.RmsInspectionId, RmsOccupancyId = inspection.RmsOccupancyId, State = (int)RmsViolationState.Open, CreatedOn = now, CreatedByUserId = userId, RowVersion = 0 };
			entity.RmsCodeSetId = RecordsPreventionGate.Trim(input.RmsCodeSetId, 36); entity.RmsCodeSectionId = RecordsPreventionGate.Trim(input.RmsCodeSectionId, 36);
			entity.Description = RecordsPreventionGate.Require(input.Description, 4000, "A violation needs a description.");
			entity.Severity = Math.Clamp(input.Severity == 0 ? 2 : input.Severity, 1, 4); entity.CorrectiveAction = RecordsPreventionGate.Trim(input.CorrectiveAction, 4000);
			entity.DueOn = input.DueOn ?? entity.DueOn ?? now.AddDays(30); entity.ModifiedOn = now; entity.RowVersion++;
			var plaintext = PlaintextSnapshot<RmsViolation>.Take(entity, RmsProtectedFields.Violations);
			await _protection.ProtectViolationAsync(departmentId, entity, existing, userId, cancellationToken);
			if (existing == null) await _violations.InsertAsync(entity, cancellationToken, true); else await _violations.UpdateAsync(entity, cancellationToken, true);
			plaintext.Restore();
			if (existing == null && inspection.State == (int)RmsInspectionState.Completed)
			{ inspection.State = (int)RmsInspectionState.ReinspectionRequired; inspection.ModifiedOn = now; inspection.RowVersion++; await _inspections.UpdateAsync(inspection, cancellationToken, true); }
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, existing == null ? "Violation recorded" : "Violation updated", inspection.RmsInspectionId, new { entity.RmsViolationId, entity.Severity, entity.DueOn }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task<RmsViolation> TransitionViolationAsync(int departmentId, string userId, string violationId, RmsViolationState target, string note, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var violation = await _violations.GetByIdForDepartmentAsync(departmentId, violationId);
			if (violation == null || violation.DeletedOn != null) throw new ArgumentException("The violation does not exist.");
			var current = (RmsViolationState)violation.State;
			var allowed = (current, target) switch
			{
				(RmsViolationState.Open, RmsViolationState.Corrected) => true,
				(RmsViolationState.Open, RmsViolationState.Escalated) => true,
				(RmsViolationState.Open, RmsViolationState.Waived) => true,
				(RmsViolationState.Escalated, RmsViolationState.Corrected) => true,
				(RmsViolationState.Escalated, RmsViolationState.Waived) => true,
				(RmsViolationState.Corrected, RmsViolationState.Verified) => true,
				(RmsViolationState.Corrected, RmsViolationState.Open) => true,
				_ => false
			};
			if (!allowed) throw new InvalidOperationException($"A violation cannot move from {current} to {target}.");
			if (target == RmsViolationState.Waived && string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Waiving a violation needs a reason.");
			var now = DateTime.UtcNow;
			violation.State = (int)target;
			if (target == RmsViolationState.Corrected) violation.CorrectedOn = now;
			if (target == RmsViolationState.Verified) { violation.VerifiedOn = now; violation.VerifiedByUserId = userId; }
			if (target == RmsViolationState.Open) { violation.CorrectedOn = null; }
			violation.ModifiedOn = now; violation.RowVersion++;
			await _violations.UpdateAsync(violation, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, $"Violation {target}", violation.RmsInspectionId, new { violationId, from = current.ToString(), to = target.ToString(), note = RecordsPreventionGate.Trim(note, 1000) }, cancellationToken: cancellationToken);
			return violation;
		}

		#endregion

		#region Sweeps

		public async Task<int> GenerateDueInspectionsAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (!await IsModuleEnabledAsync(departmentId)) return 0;
			var generated = 0;
			var programs = ((await _programs.GetForDepartmentAsync(departmentId, false)) ?? Enumerable.Empty<RmsInspectionProgram>()).Where(p => p.FrequencyMonths > 0).ToList();
			if (programs.Count == 0) return 0;
			var occupancies = ((await _occupancies.GetAllLiveAsync(departmentId)) ?? Enumerable.Empty<RmsOccupancy>()).Where(o => o.Status == (int)RmsOccupancyStatus.Active).ToList();
			foreach (var program in programs)
			{
				var types = (program.OccupancyTypesCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t.Trim(), out var v) ? v : -1).Where(v => v >= 0).ToHashSet();
				var lastCompleted = await _inspections.GetLastCompletedByOccupancyAsync(departmentId, program.RmsInspectionProgramId);
				var open = ((await _inspections.GetOpenForProgramAsync(departmentId, program.RmsInspectionProgramId)) ?? Enumerable.Empty<RmsInspection>()).Select(i => i.RmsOccupancyId).ToHashSet(StringComparer.Ordinal);
				foreach (var occupancy in occupancies)
				{
					if (types.Count > 0 && !types.Contains(occupancy.OccupancyType)) continue;
					if (open.Contains(occupancy.RmsOccupancyId)) continue;
					var due = !lastCompleted.TryGetValue(occupancy.RmsOccupancyId, out var last) || last.AddMonths(program.FrequencyMonths) <= utcNow;
					if (!due) continue;
					await CreateScheduledAsync(departmentId, null, occupancy, program, utcNow, null, null, cancellationToken);
					generated++;
					if (generated >= SweepBatch) return generated;
				}
			}
			return generated;
		}

		public async Task<int> EmitOverdueViolationsAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (!await IsModuleEnabledAsync(departmentId)) return 0;
			var emitted = 0;
			var overdue = (await _violations.GetOverdueNotEmittedAsync(departmentId, utcNow, SweepBatch))?.ToList() ?? new List<RmsViolation>();
			foreach (var violation in overdue)
			{
				long outboxId;
				_unitOfWork.CreateOrGetConnection();
				try
				{
					violation.OverdueEmittedOn = utcNow; violation.ModifiedOn = utcNow; violation.RowVersion++;
					await _violations.UpdateAsync(violation, cancellationToken, true);
					outboxId = await EnqueueViolationAsync(violation, utcNow, cancellationToken);
					_unitOfWork.CommitChanges();
				}
				catch { _unitOfWork.DiscardChanges(); throw; }
				await _outbox.DispatchAfterCommitAsync(new[] { outboxId }, cancellationToken);
				emitted++;
			}
			return emitted;
		}

		#endregion

		#region Events

		/// <summary>inspection.* (trigger 161): identity, program, result and violation counts — never the notes or the signature.</summary>
		private async Task<long> EnqueueInspectionAsync(RmsInspection inspection, RmsOccupancy occupancy, RmsInspectionProgram program, int violationCount, int criticalCount, CancellationToken cancellationToken)
		{
			var entry = await _outbox.EnqueueAsync(inspection.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = WorkflowTriggerEventType.RecordInspectionCompleted.ToString(), SchemaVersion = 1, AggregateType = InspectionAggregate, AggregateId = inspection.RmsInspectionId, AggregateVersion = (int)inspection.RowVersion,
				Trigger = WorkflowTriggerEventType.RecordInspectionCompleted,
				Payload = new Dictionary<string, object>
				{
					["record"] = PreventionRecordBlock(inspection.DepartmentId),
					["inspection"] = new
					{
						id = inspection.RmsInspectionId, number = inspection.InspectionNumber, occupancy_id = occupancy.RmsOccupancyId, occupancy_number = occupancy.OccupancyNumber, occupancy_name = occupancy.Name,
						program_id = program?.RmsInspectionProgramId, program_name = program?.Name, state = ((RmsInspectionState)inspection.State).ToString(), result = ((RmsInspectionResult)inspection.Result).ToString(),
						scheduled_on = inspection.ScheduledOn, completed_on = inspection.CompletedOn, inspector_user_id = inspection.InspectorUserId, violation_count = violationCount, critical_violation_count = criticalCount,
						is_reinspection = inspection.ParentInspectionId != null
					},
					["protection"] = IncidentReportsService.ProtectionBlock(await _protection.GetCatalogVersionAsync(inspection.DepartmentId))
				},
				CorrelationId = inspection.RmsInspectionId, OriginClient = RmsOriginClient.Web
			}, cancellationToken);
			return entry.DomainEventOutboxId;
		}

		/// <summary>violation.* (trigger 162): identity, citation, severity and lateness — never the description or corrective text.</summary>
		private async Task<long> EnqueueViolationAsync(RmsViolation violation, DateTime utcNow, CancellationToken cancellationToken)
		{
			RmsCodeSection section = violation.RmsCodeSectionId == null ? null : await _codeSections.GetByIdForDepartmentAsync(violation.DepartmentId, violation.RmsCodeSectionId);
			var occupancy = await _occupancies.GetByIdForDepartmentAsync(violation.DepartmentId, violation.RmsOccupancyId);
			var entry = await _outbox.EnqueueAsync(violation.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = WorkflowTriggerEventType.RecordViolationOverdue.ToString(), SchemaVersion = 1, AggregateType = ViolationAggregate, AggregateId = violation.RmsViolationId, AggregateVersion = (int)violation.RowVersion,
				Trigger = WorkflowTriggerEventType.RecordViolationOverdue,
				Payload = new Dictionary<string, object>
				{
					["record"] = PreventionRecordBlock(violation.DepartmentId),
					["violation"] = new
					{
						id = violation.RmsViolationId, inspection_id = violation.RmsInspectionId, occupancy_id = violation.RmsOccupancyId, occupancy_number = occupancy?.OccupancyNumber, occupancy_name = occupancy?.Name,
						code_set_id = violation.RmsCodeSetId, code_section_id = violation.RmsCodeSectionId, code_section_number = section?.SectionNumber, severity = ((RmsViolationSeverity)violation.Severity).ToString(),
						state = ((RmsViolationState)violation.State).ToString(), due_on = violation.DueOn, days_overdue = violation.DueOn.HasValue ? (int)Math.Floor((utcNow - violation.DueOn.Value).TotalDays) : 0
					},
					["protection"] = IncidentReportsService.ProtectionBlock(await _protection.GetCatalogVersionAsync(violation.DepartmentId))
				},
				CorrelationId = violation.RmsViolationId, OriginClient = RmsOriginClient.System
			}, cancellationToken);
			return entry.DomainEventOutboxId;
		}

		/// <summary>Prevention events are not Records; the record block carries only the kind and department so templates keep a stable namespace.</summary>
		public static object PreventionRecordBlock(int departmentId) => new { id = (string)null, kind = "Prevention", department_id = departmentId, state = (string)null };

		#endregion
	}
}
