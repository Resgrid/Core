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
	/// Apparatus and vehicles ("units"). Each new unit uses a plan seat (section 11.6); its type is stored by name, its
	/// station is a group, its seats become unit roles and kept columns become unit custom fields. Rollback soft-deletes
	/// (calls, states and logs keep referring to the unit).
	/// </summary>
	public sealed class UnitsImportWriter : ImportRecordWriter<Unit>, IImportExporter
	{
		private readonly IUnitsService _units;
		private readonly IUserDefinedFieldsService _customFields;
		private readonly IDepartmentGroupsService _groups;
		private Dictionary<int, string> _stationNames;

		private static readonly IReadOnlyList<ImportFieldBinding<Unit>> Fields = new[]
		{
			new ImportFieldBinding<Unit>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<Unit>("vin", r => r.VIN, (r, v) => r.VIN = v),
			new ImportFieldBinding<Unit>("plate_number", r => r.PlateNumber, (r, v) => r.PlateNumber = v),
			new ImportFieldBinding<Unit>("four_wheel_drive", r => ImportCanonical.Bool(r.FourWheel), (r, v) => r.FourWheel = ImportCanonical.ToNullableBool(v)),
			new ImportFieldBinding<Unit>("special_permit", r => ImportCanonical.Bool(r.SpecialPermit), (r, v) => r.SpecialPermit = ImportCanonical.ToNullableBool(v))
		};

		public UnitsImportWriter(IUnitsService units, IUserDefinedFieldsService customFields, IDepartmentGroupsService groups)
		{
			_units = units;
			_customFields = customFields;
			_groups = groups;
		}

		public override string EntityKey => "units";
		public override ImportEffects AffectedEffects =>
			ImportEffects.UnitVisibility | ImportEffects.UnitsCache | ImportEffects.LimitsCache | ImportEffects.SearchRebuild | ImportEffects.AppsResync;
		protected override IReadOnlyList<ImportFieldBinding<Unit>> Bindings => Fields;
		protected override bool UsesUnitSeat => true;

		protected override async Task<List<Unit>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _units.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>()).Where(u => u.DepartmentId == departmentId && !u.IsDeleted).ToList();

		protected override async Task<Unit> GetAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var id = ImportCanonical.ToNullableInt(key);
			if (id == null)
				return null;
			var unit = await _units.GetUnitByIdAsync(id.Value);
			return unit != null && unit.DepartmentId == context.DepartmentId && !unit.IsDeleted ? unit : null;
		}

		protected override string KeyOf(Unit record) => ImportCanonical.Int(record.UnitId);
		protected override string NaturalKeyOf(Unit record) => record.Name;
		protected override Unit New(ImportWriteContext context, ImportCanonicalRow row) => new Unit { DepartmentId = context.DepartmentId };

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, Unit record, ImportCanonicalRow row, CancellationToken cancellationToken)
		{
			var typeToken = row.Text("type");
			if (!string.IsNullOrWhiteSpace(typeToken))
			{
				if (ImportReferenceToken.Is(typeToken, ImportReferenceToken.Label))
				{
					record.Type = ImportReferenceToken.Value(typeToken, ImportReferenceToken.Label);
				}
				else
				{
					var typeId = await ResolveIntAsync(context, "unit-types", typeToken, cancellationToken);
					var type = typeId == null ? null : await _units.GetUnitTypeByIdAsync(typeId.Value);
					if (type == null || type.DepartmentId != context.DepartmentId)
						return "IMP-REF-UNRESOLVED";
					// Units store their type by name.
					record.Type = type.Type;
				}
			}

			var stationToken = row.Text("station");
			if (!string.IsNullOrWhiteSpace(stationToken) && !ImportReferenceToken.Is(stationToken, ImportReferenceToken.Label))
			{
				record.StationGroupId = await ResolveIntAsync(context, "groups", stationToken, cancellationToken);
				if (record.StationGroupId == null)
					return "IMP-REF-UNRESOLVED";
			}

			return null;
		}

		protected override Task<Unit> SaveAsync(ImportWriteContext context, Unit record, CancellationToken cancellationToken)
		{
			// Seats are written after the unit exists; never cascade them from the unit row.
			record.Roles = null;
			return _units.SaveUnitAsync(record, context.Effects, cancellationToken);
		}

		protected override async Task AfterSaveAsync(ImportWriteContext context, Unit record, ImportCanonicalRow row, bool created, CancellationToken cancellationToken)
		{
			if (created)
			{
				// A repeated name is another seat of the same kind ("Firefighter; Firefighter" is two seats), so no de-duplication.
				var seats = row.List("seats").Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
				if (seats.Count > 0)
					await _units.SetRolesForUnitAsync(record.UnitId, seats.Select(s => new UnitRole { Name = s.Length > 100 ? s.Substring(0, 100) : s }).ToList(), cancellationToken);
			}

			var values = await ImportCustomFields.BuildValuesAsync(_customFields, context.DepartmentId, UdfEntityType.Unit, row.CustomFields);
			if (values.Count > 0)
				await _customFields.SaveFieldValuesForEntityAsync(context.DepartmentId, (int)UdfEntityType.Unit, ImportCanonical.Int(record.UnitId), values,
					context.ActingUserId, true, false, context.Effects, cancellationToken);
		}

		protected override async Task ExportReferencesAsync(int departmentId, Unit record, Dictionary<string, string> row, CancellationToken cancellationToken)
		{
			_stationNames ??= (await _groups.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId) ?? new List<DepartmentGroup>())
				.GroupBy(g => g.DepartmentGroupId).ToDictionary(g => g.Key, g => g.First().Name);
			row["type"] = record.Type;
			row["station"] = record.StationGroupId.HasValue && _stationNames.TryGetValue(record.StationGroupId.Value, out var station) ? station : null;
		}

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, Unit record, CancellationToken cancellationToken) =>
			await _units.DeleteUnitAsync(record.UnitId, context.ActingUserId, cancellationToken) ? ImportRollbackOutcome.SoftDeleted : ImportRollbackOutcome.ManualReconciliation;
	}
}
