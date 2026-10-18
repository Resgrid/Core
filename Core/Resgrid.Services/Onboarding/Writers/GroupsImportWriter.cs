using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding.Writers
{
	/// <summary>
	/// Stations and organizational groups ("groups"). The address is created with the group; the parent may be a row
	/// later in the same file, so parents are set after the whole dataset is written (deferred work).
	/// </summary>
	public sealed class GroupsImportWriter : ImportRecordWriter<DepartmentGroup>, IImportImplicitCreator, IImportDeferredWork, IImportExporter
	{
		private static readonly string[] Types = { "organizational", "station" };

		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;

		private static readonly IReadOnlyList<ImportFieldBinding<DepartmentGroup>> Fields = new[]
		{
			new ImportFieldBinding<DepartmentGroup>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<DepartmentGroup>("type", r => ImportCanonical.Code(Types, r.Type ?? (int)DepartmentGroupTypes.Orginizational, 1),
				(r, v) => r.Type = ImportCanonical.Value(Types, v, 1, (int)DepartmentGroupTypes.Orginizational)),
			new ImportFieldBinding<DepartmentGroup>("latitude", r => r.Latitude, (r, v) => r.Latitude = v),
			new ImportFieldBinding<DepartmentGroup>("longitude", r => r.Longitude, (r, v) => r.Longitude = v)
		};

		public GroupsImportWriter(IDepartmentGroupsService groups, IUnitsService units)
		{
			_groups = groups;
			_units = units;
		}

		public override string EntityKey => "groups";
		public override ImportEffects AffectedEffects =>
			ImportEffects.GroupVisibility | ImportEffects.PersonnelVisibility | ImportEffects.UnitVisibility | ImportEffects.GroupsCache | ImportEffects.SearchRebuild | ImportEffects.AppsResync;
		protected override IReadOnlyList<ImportFieldBinding<DepartmentGroup>> Bindings => Fields;

		protected override async Task<List<DepartmentGroup>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _groups.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId) ?? new List<DepartmentGroup>()).Where(g => g.DepartmentId == departmentId).ToList();

		protected override async Task<DepartmentGroup> GetAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var id = ImportCanonical.ToNullableInt(key);
			if (id == null)
				return null;
			var group = await _groups.GetGroupByIdAsync(id.Value, true);
			return group != null && group.DepartmentId == context.DepartmentId ? group : null;
		}

		protected override string KeyOf(DepartmentGroup record) => ImportCanonical.Int(record.DepartmentGroupId);
		protected override string NaturalKeyOf(DepartmentGroup record) => record.Name;

		protected override DepartmentGroup New(ImportWriteContext context, ImportCanonicalRow row)
		{
			var group = new DepartmentGroup { DepartmentId = context.DepartmentId, Type = (int)DepartmentGroupTypes.Orginizational };
			if (new[] { "street", "city", "state", "postal_code", "country" }.Any(f => !string.IsNullOrWhiteSpace(row.Text(f))))
				group.Address = new Address
				{
					Address1 = row.Text("street") ?? string.Empty,
					City = row.Text("city") ?? string.Empty,
					State = row.Text("state") ?? string.Empty,
					PostalCode = row.Text("postal_code") ?? string.Empty,
					Country = row.Text("country") ?? string.Empty
				};
			return group;
		}

		protected override async Task<DepartmentGroup> SaveAsync(ImportWriteContext context, DepartmentGroup record, CancellationToken cancellationToken)
		{
			// Never cascade members or children from an import write.
			record.Members = null;
			record.Children = null;
			return await _groups.SaveAsync(record, context.Effects, cancellationToken);
		}

		protected override async Task<bool> HasOutsideActivityAsync(ImportWriteContext context, DepartmentGroup record, CancellationToken cancellationToken)
		{
			// Members and units imported into the group were rolled back first (reverse dependency order); anything left was added since.
			if ((await _groups.GetAllMembersForGroupAsync(record.DepartmentGroupId))?.Any() == true)
				return true;
			if ((await _units.GetAllUnitsForGroupAsync(record.DepartmentGroupId))?.Any(u => !u.IsDeleted) == true)
				return true;
			return (await LoadAsync(context.DepartmentId, cancellationToken)).Any(g => g.ParentDepartmentGroupId == record.DepartmentGroupId);
		}

		private Dictionary<int, string> _namesForExport;

		protected override async Task ExportReferencesAsync(int departmentId, DepartmentGroup record, Dictionary<string, string> row, CancellationToken cancellationToken)
		{
			_namesForExport ??= (await LoadAsync(departmentId, cancellationToken)).GroupBy(g => g.DepartmentGroupId).ToDictionary(g => g.Key, g => g.First().Name);
			row["parent"] = record.ParentDepartmentGroupId.HasValue && _namesForExport.TryGetValue(record.ParentDepartmentGroupId.Value, out var parent) ? parent : null;
		}

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, DepartmentGroup record, CancellationToken cancellationToken) =>
			await _groups.DeleteGroupByIdAsync(record.DepartmentGroupId, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;

		public async Task<int> CompleteDeferredAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> writtenRows, ImportJournalWriter journal,
			CancellationToken cancellationToken)
		{
			var changed = 0;
			foreach (var row in writtenRows.Where(r => r.TargetKey != null && !string.IsNullOrWhiteSpace(r.Text("parent"))))
			{
				var parentId = await ResolveIntAsync(context, EntityKey, row.Text("parent"), cancellationToken);
				var group = await GetAsync(context, row.TargetKey, cancellationToken);
				if (parentId == null || group == null || group.ParentDepartmentGroupId == parentId || parentId.Value == group.DepartmentGroupId)
					continue;

				// A matched (linked) group keeps its place in the hierarchy; only groups the import created are placed.
				if (row.Decision != ImportRowDecision.Create)
					continue;

				group.ParentDepartmentGroupId = parentId;
				group.Address = null;
				await SaveAsync(context, group, cancellationToken);
				changed++;
			}

			return changed;
		}
	}
}
