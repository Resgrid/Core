using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding.Writers
{
	// Wave-1 catalog writers (wizard plan section 7.4): small department lists other files refer to by name. Each one
	// matches on the exact unique name, accepts "create new" decisions from other files, and rolls back through the
	// owning service's delete path when nothing outside the import uses the record.

	/// <summary>Personnel roles ("roles").</summary>
	public sealed class RolesImportWriter : ImportRecordWriter<PersonnelRole>, IImportImplicitCreator, IImportExporter
	{
		private readonly IPersonnelRolesService _roles;

		private static readonly IReadOnlyList<ImportFieldBinding<PersonnelRole>> Fields = new[]
		{
			new ImportFieldBinding<PersonnelRole>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<PersonnelRole>("description", r => r.Description, (r, v) => r.Description = v)
		};

		public RolesImportWriter(IPersonnelRolesService roles)
		{
			_roles = roles;
		}

		public override string EntityKey => "roles";
		public override ImportEffects AffectedEffects => ImportEffects.RoleVisibility | ImportEffects.SearchRebuild;
		protected override IReadOnlyList<ImportFieldBinding<PersonnelRole>> Bindings => Fields;

		protected override async Task<List<PersonnelRole>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			await _roles.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>();

		protected override string KeyOf(PersonnelRole record) => ImportCanonical.Int(record.PersonnelRoleId);
		protected override string NaturalKeyOf(PersonnelRole record) => record.Name;
		protected override PersonnelRole New(ImportWriteContext context, ImportCanonicalRow row) => new PersonnelRole { DepartmentId = context.DepartmentId };

		protected override Task<PersonnelRole> SaveAsync(ImportWriteContext context, PersonnelRole record, CancellationToken cancellationToken)
		{
			// A role saved here never carries members: people are added by the personnel writer.
			record.Users = null;
			return _roles.SaveRoleAsync(record, context.Effects, cancellationToken, context.ActingUserId);
		}

		protected override async Task<bool> HasOutsideActivityAsync(ImportWriteContext context, PersonnelRole record, CancellationToken cancellationToken) =>
			(await _roles.GetAllMembersOfRoleAsync(record.PersonnelRoleId))?.Any() == true;

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, PersonnelRole record, CancellationToken cancellationToken) =>
			await _roles.DeleteRoleByIdAsync(record.PersonnelRoleId, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
	}

	/// <summary>
	/// The base types of call types, unit types and call priorities (CallType.BaseType, UnitType.BaseType,
	/// DepartmentCallPriority.BaseType): the contract's choice codes in enum order. Blank keeps None (-1): like the Types
	/// screens, the import never sets a class from a name guess; the department chooses it.
	/// </summary>
	internal static class ImportBaseTypes
	{
		public static readonly string[] CallTypeClasses =
		{
			"fire", "medical", "rescue", "traffic-collision", "hazardous-materials", "alarm", "service-call", "law-enforcement", "disaster-weather",
			"mutual-aid", "non-emergency", "other"
		};

		public static readonly string[] UnitTypeClasses =
		{
			"engine", "aerial", "rescue", "tanker", "ambulance", "medical-response", "wildland", "hazardous-materials", "command", "support",
			"watercraft", "aircraft", "patrol", "other"
		};

		public static readonly string[] Priorities = { "low", "medium", "high", "emergency" };

		/// <summary>The code of a 1-based class (None is -1, and so no code).</summary>
		public static string Code(string[] codes, int value, int offset) => value < offset ? null : ImportCanonical.Code(codes, value, offset);

		public static int Value(string[] codes, string code, int offset) =>
			string.IsNullOrWhiteSpace(code) ? -1 : ImportCanonical.Value(codes, code, offset, fallback: -1);
	}

	/// <summary>Call types ("call-types").</summary>
	public sealed class CallTypesImportWriter : ImportRecordWriter<CallType>, IImportImplicitCreator, IImportExporter
	{
		private readonly ICallsService _calls;

		private static readonly IReadOnlyList<ImportFieldBinding<CallType>> Fields = new[]
		{
			new ImportFieldBinding<CallType>("name", r => r.Type, (r, v) => r.Type = v),
			new ImportFieldBinding<CallType>("base_type", r => ImportBaseTypes.Code(ImportBaseTypes.CallTypeClasses, r.BaseType, 1),
				(r, v) => r.BaseType = ImportBaseTypes.Value(ImportBaseTypes.CallTypeClasses, v, 1))
		};

		public CallTypesImportWriter(ICallsService calls)
		{
			_calls = calls;
		}

		public override string EntityKey => "call-types";
		public override ImportEffects AffectedEffects => ImportEffects.CallTypesCache | ImportEffects.AppsResync;
		protected override IReadOnlyList<ImportFieldBinding<CallType>> Bindings => Fields;

		protected override async Task<List<CallType>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _calls.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>()).Where(t => t.DepartmentId == departmentId).ToList();

		protected override string KeyOf(CallType record) => ImportCanonical.Int(record.CallTypeId);
		protected override string NaturalKeyOf(CallType record) => record.Type;
		protected override CallType New(ImportWriteContext context, ImportCanonicalRow row) => new CallType { DepartmentId = context.DepartmentId };

		protected override Task<CallType> SaveAsync(ImportWriteContext context, CallType record, CancellationToken cancellationToken) =>
			_calls.SaveCallTypeAsync(record, cancellationToken);

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, CallType record, CancellationToken cancellationToken) =>
			await _calls.DeleteCallTypeAsync(record.CallTypeId, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
	}

	/// <summary>Call priorities ("call-priorities"). Importing a default moves the default flag (journaled, restored by rollback).</summary>
	public sealed class CallPrioritiesImportWriter : ImportRecordWriter<DepartmentCallPriority>, IImportExporter
	{
		private readonly ICallsService _calls;

		private static readonly IReadOnlyList<ImportFieldBinding<DepartmentCallPriority>> Fields = new[]
		{
			new ImportFieldBinding<DepartmentCallPriority>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<DepartmentCallPriority>("color", r => r.Color, (r, v) => r.Color = v),
			new ImportFieldBinding<DepartmentCallPriority>("sort", r => ImportCanonical.Int(r.Sort), (r, v) => r.Sort = ImportCanonical.ToInt(v)),
			new ImportFieldBinding<DepartmentCallPriority>("is_default", r => ImportCanonical.Bool(r.IsDefault), (r, v) => r.IsDefault = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentCallPriority>("dispatch_personnel", r => ImportCanonical.Bool(r.DispatchPersonnel), (r, v) => r.DispatchPersonnel = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentCallPriority>("dispatch_units", r => ImportCanonical.Bool(r.DispatchUnits), (r, v) => r.DispatchUnits = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentCallPriority>("notify_everyone", r => ImportCanonical.Bool(r.ForceNotifyAllPersonnel), (r, v) => r.ForceNotifyAllPersonnel = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentCallPriority>("base_type", r => ImportBaseTypes.Code(ImportBaseTypes.Priorities, r.BaseType, 0),
				(r, v) => r.BaseType = ImportBaseTypes.Value(ImportBaseTypes.Priorities, v, 0))
		};

		public CallPrioritiesImportWriter(ICallsService calls)
		{
			_calls = calls;
		}

		public override string EntityKey => "call-priorities";
		public override ImportEffects AffectedEffects => ImportEffects.CallTypesCache | ImportEffects.AppsResync;
		protected override IReadOnlyList<ImportFieldBinding<DepartmentCallPriority>> Bindings => Fields;

		protected override async Task<List<DepartmentCallPriority>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			// The service falls back to the system defaults when a department has none; those are not department records.
			(await _calls.GetCallPrioritiesForDepartmentAsync(departmentId, true) ?? new List<DepartmentCallPriority>())
				.Where(p => p.DepartmentId == departmentId && p.DepartmentCallPriorityId > 0 && !p.IsDeleted).ToList();

		protected override string KeyOf(DepartmentCallPriority record) => ImportCanonical.Int(record.DepartmentCallPriorityId);
		protected override string NaturalKeyOf(DepartmentCallPriority record) => record.Name;

		protected override DepartmentCallPriority New(ImportWriteContext context, ImportCanonicalRow row) =>
			new DepartmentCallPriority { DepartmentId = context.DepartmentId, DispatchPersonnel = true, DispatchUnits = true };

		protected override Task<DepartmentCallPriority> SaveAsync(ImportWriteContext context, DepartmentCallPriority record, CancellationToken cancellationToken) =>
			_calls.SaveCallPriorityAsync(record, cancellationToken);

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, DepartmentCallPriority record, CancellationToken cancellationToken)
		{
			// Calls keep their priority id: priorities are soft-deleted, as the Types screen does.
			record.IsDeleted = true;
			record.IsDefault = false;
			await _calls.SaveCallPriorityAsync(record, cancellationToken);
			return ImportRollbackOutcome.SoftDeleted;
		}

		// Replace: a priority the file does not contain is soft-deleted, and rollback clears the flag (calls keep its id).
		protected override bool CanRetireRecords => true;

		protected override bool IsRetired(DepartmentCallPriority record) => record.IsDeleted;

		protected override async Task<DepartmentCallPriority> GetIncludingRetiredAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var priority = await _calls.GetCallPrioritiesByIdAsync(context.DepartmentId, ImportCanonical.ToInt(key), true);
			return priority?.DepartmentId == context.DepartmentId ? priority : null;
		}

		protected override async Task RestoreRetiredAsync(ImportWriteContext context, DepartmentCallPriority record, CancellationToken cancellationToken)
		{
			record.IsDeleted = false;
			await _calls.SaveCallPriorityAsync(record, cancellationToken);
		}

		public override async Task<ImportRowWriteResult> WriteRowAsync(ImportWriteContext context, ImportCanonicalRow row, ImportJournalWriter journal,
			CancellationToken cancellationToken)
		{
			var result = await base.WriteRowAsync(context, row, journal, cancellationToken);
			if ((result.Outcome == ImportRowOutcome.Created || result.Outcome == ImportRowOutcome.Updated) && row.Bool("is_default") == true)
			{
				// One default per department: the previous default gives it up, journaled so rollback restores it.
				foreach (var other in (await LoadAsync(context.DepartmentId, cancellationToken)).Where(p => p.IsDefault && KeyOf(p) != result.TargetKey))
				{
					other.IsDefault = false;
					await _calls.SaveCallPriorityAsync(other, cancellationToken);
					await journal.AppendAsync(EntityKey, KeyOf(other), ImportJournalOperation.Update, "{\"is_default\":\"true\"}", cancellationToken: cancellationToken);
				}
			}

			return result;
		}
	}

	/// <summary>Points-of-interest types ("poi-types").</summary>
	public sealed class PoiTypesImportWriter : ImportRecordWriter<PoiType>, IImportImplicitCreator, IImportExporter
	{
		private readonly IMappingService _mapping;

		private static readonly IReadOnlyList<ImportFieldBinding<PoiType>> Fields = new[]
		{
			new ImportFieldBinding<PoiType>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<PoiType>("color", r => r.Color, (r, v) => r.Color = v),
			new ImportFieldBinding<PoiType>("is_destination", r => ImportCanonical.Bool(r.IsDestination), (r, v) => r.IsDestination = ImportCanonical.ToBool(v))
		};

		public PoiTypesImportWriter(IMappingService mapping)
		{
			_mapping = mapping;
		}

		public override string EntityKey => "poi-types";
		public override ImportEffects AffectedEffects => ImportEffects.AppsResync | ImportEffects.SearchRebuild;
		protected override IReadOnlyList<ImportFieldBinding<PoiType>> Bindings => Fields;

		protected override async Task<List<PoiType>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _mapping.GetPOITypesForDepartmentAsync(departmentId) ?? new List<PoiType>()).Where(t => t.DepartmentId == departmentId).ToList();

		protected override string KeyOf(PoiType record) => ImportCanonical.Int(record.PoiTypeId);
		protected override string NaturalKeyOf(PoiType record) => record.Name;

		// The defaults the Add POI Type screen starts from.
		protected override PoiType New(ImportWriteContext context, ImportCanonicalRow row) =>
			new PoiType { DepartmentId = context.DepartmentId, Marker = "MAP_PIN", Image = string.Empty };

		protected override Task<PoiType> SaveAsync(ImportWriteContext context, PoiType record, CancellationToken cancellationToken) =>
			_mapping.SavePOITypeAsync(record, context.Effects, cancellationToken);

		protected override async Task<bool> HasOutsideActivityAsync(ImportWriteContext context, PoiType record, CancellationToken cancellationToken) =>
			(await _mapping.GetPOIsForDepartmentAsync(context.DepartmentId))?.Any(p => p.PoiTypeId == record.PoiTypeId) == true;

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, PoiType record, CancellationToken cancellationToken) =>
			await _mapping.DeletePOITypeAsync(record.PoiTypeId, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
	}

	/// <summary>Points of interest ("pois"). Matched on name and position; the type is a reference.</summary>
	public sealed class PoisImportWriter : ImportRecordWriter<Poi>
	{
		private readonly IMappingService _mapping;

		private static readonly IReadOnlyList<ImportFieldBinding<Poi>> Fields = new[]
		{
			new ImportFieldBinding<Poi>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<Poi>("latitude", r => Coordinate(r.Latitude), (r, v) => r.Latitude = ToDouble(v)),
			new ImportFieldBinding<Poi>("longitude", r => Coordinate(r.Longitude), (r, v) => r.Longitude = ToDouble(v)),
			new ImportFieldBinding<Poi>("address", r => r.Address, (r, v) => r.Address = v),
			new ImportFieldBinding<Poi>("note", r => r.Note, (r, v) => r.Note = v)
		};

		public PoisImportWriter(IMappingService mapping)
		{
			_mapping = mapping;
		}

		public override string EntityKey => "pois";
		public override ImportEffects AffectedEffects => ImportEffects.AppsResync | ImportEffects.SearchRebuild;
		protected override IReadOnlyList<ImportFieldBinding<Poi>> Bindings => Fields;

		private static string Coordinate(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

		private static double ToDouble(string value) =>
			double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

		protected override async Task<List<Poi>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			await _mapping.GetPOIsForDepartmentAsync(departmentId) ?? new List<Poi>();

		protected override string KeyOf(Poi record) => ImportCanonical.Int(record.PoiId);
		protected override string NaturalKeyOf(Poi record) => record.Name;

		// The same name at the same position is the same place; the type is a reference, so it is not part of the match.
		protected override string MatchKeyOf(Poi record) => Composite(record.Name, Coordinate(record.Latitude), Coordinate(record.Longitude));

		protected override string MatchKeyOf(ImportCanonicalRow row) =>
			Composite(row.Text("name"), Coordinate(ToDouble(row.Text("latitude"))), Coordinate(ToDouble(row.Text("longitude"))));

		protected override Poi New(ImportWriteContext context, ImportCanonicalRow row) => new Poi();

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, Poi record, ImportCanonicalRow row, CancellationToken cancellationToken)
		{
			var type = await ResolveIntAsync(context, "poi-types", row.Text("type"), cancellationToken);
			if (type == null)
				return "IMP-REF-UNRESOLVED";
			record.PoiTypeId = type.Value;
			return null;
		}

		protected override Task<Poi> SaveAsync(ImportWriteContext context, Poi record, CancellationToken cancellationToken) =>
			_mapping.SavePOIAsync(record, context.Effects, cancellationToken);

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, Poi record, CancellationToken cancellationToken) =>
			await _mapping.DeletePOIAsync(record.PoiId, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
	}

	/// <summary>Certification types ("certification-types").</summary>
	public sealed class CertificationTypesImportWriter : ImportRecordWriter<DepartmentCertificationType>, IImportImplicitCreator, IImportExporter
	{
		private static readonly string[] Categories =
			{ "fire", "ems", "sar", "wildland", "emergency-management", "industrial", "security", "medical", "driver", "vehicle", "other" };
		private static readonly string[] AppliesTo = { "person", "unit" };

		private readonly ICertificationService _certifications;

		private static readonly IReadOnlyList<ImportFieldBinding<DepartmentCertificationType>> Fields = new[]
		{
			new ImportFieldBinding<DepartmentCertificationType>("name", r => r.Type, (r, v) => r.Type = v),
			new ImportFieldBinding<DepartmentCertificationType>("code", r => r.Code, (r, v) => r.Code = v),
			new ImportFieldBinding<DepartmentCertificationType>("category", r => ImportCanonical.Code(Categories, r.Category),
				(r, v) => r.Category = ImportCanonical.Value(Categories, v, fallback: (int)CertificationCategories.Other)),
			new ImportFieldBinding<DepartmentCertificationType>("applies_to", r => ImportCanonical.Code(AppliesTo, r.AppliesTo), (r, v) => r.AppliesTo = ImportCanonical.Value(AppliesTo, v)),
			new ImportFieldBinding<DepartmentCertificationType>("issuing_authority", r => r.IssuingAuthority, (r, v) => r.IssuingAuthority = v),
			new ImportFieldBinding<DepartmentCertificationType>("validity_months", r => ImportCanonical.Int(r.DefaultValidityMonths), (r, v) => r.DefaultValidityMonths = ImportCanonical.ToNullableInt(v)),
			new ImportFieldBinding<DepartmentCertificationType>("never_expires", r => ImportCanonical.Bool(r.NeverExpires), (r, v) => r.NeverExpires = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentCertificationType>("requires_verification", r => ImportCanonical.Bool(r.RequiresVerification), (r, v) => r.RequiresVerification = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentCertificationType>("description", r => r.Description, (r, v) => r.Description = v)
		};

		public CertificationTypesImportWriter(ICertificationService certifications)
		{
			_certifications = certifications;
		}

		public override string EntityKey => "certification-types";
		public override ImportEffects AffectedEffects => ImportEffects.SearchRebuild;
		protected override IReadOnlyList<ImportFieldBinding<DepartmentCertificationType>> Bindings => Fields;

		protected override async Task<List<DepartmentCertificationType>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _certifications.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => t.DepartmentId == departmentId && !t.IsDeleted).ToList();

		protected override string KeyOf(DepartmentCertificationType record) => ImportCanonical.Int(record.DepartmentCertificationTypeId);
		protected override string NaturalKeyOf(DepartmentCertificationType record) => record.Type;

		protected override DepartmentCertificationType New(ImportWriteContext context, ImportCanonicalRow row) => new DepartmentCertificationType
		{
			DepartmentId = context.DepartmentId,
			Category = (int)CertificationCategories.Other,
			AppliesTo = (int)CertificationAppliesTo.Person,
			IsActive = true
		};

		protected override Task<DepartmentCertificationType> SaveAsync(ImportWriteContext context, DepartmentCertificationType record, CancellationToken cancellationToken) =>
			_certifications.SaveCertificationTypeAsync(record, context.ActingUserId, context.Effects, cancellationToken);

		protected override async Task<bool> HasOutsideActivityAsync(ImportWriteContext context, DepartmentCertificationType record, CancellationToken cancellationToken) =>
			(await _certifications.GetCertificationsForDepartmentAsync(context.DepartmentId))?
				.Any(c => !c.IsDeleted && c.DepartmentCertificationTypeId == record.DepartmentCertificationTypeId) == true;

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, DepartmentCertificationType record, CancellationToken cancellationToken) =>
			await _certifications.DeleteCertificationTypeByIdAsync(record.DepartmentCertificationTypeId, context.ActingUserId, cancellationToken)
				? ImportRollbackOutcome.Deleted
				: ImportRollbackOutcome.ManualReconciliation;

		// Replace: an unused type the file does not contain is soft-deleted (as the Certifications screen does) and restored by rollback.
		protected override bool CanRetireRecords => true;

		protected override bool IsRetired(DepartmentCertificationType record) => record.IsDeleted;

		protected override async Task<string> RetireBlockerAsync(ImportWriteContext context, DepartmentCertificationType record, CancellationToken cancellationToken) =>
			await _certifications.IsCertificationTypeInUseAsync(record.DepartmentCertificationTypeId) ? "IMP-REPLACE-IN-USE" : null;

		protected override async Task<DepartmentCertificationType> GetIncludingRetiredAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var type = await _certifications.GetCertificationTypeByIdAsync(ImportCanonical.ToInt(key));
			return type?.DepartmentId == context.DepartmentId ? type : null;
		}

		protected override async Task RestoreRetiredAsync(ImportWriteContext context, DepartmentCertificationType record, CancellationToken cancellationToken)
		{
			record.IsDeleted = false;
			record.IsActive = true;
			await _certifications.SaveCertificationTypeAsync(record, context.ActingUserId, context.Effects, cancellationToken);
		}
	}

	/// <summary>Unit types ("unit-types"); the status set refers to a unit custom-status set.</summary>
	public sealed class UnitTypesImportWriter : ImportRecordWriter<UnitType>, IImportImplicitCreator, IImportExporter
	{
		private readonly IUnitsService _units;
		private readonly ICustomStateService _customStates;

		private static readonly IReadOnlyList<ImportFieldBinding<UnitType>> Fields = new[]
		{
			new ImportFieldBinding<UnitType>("name", r => r.Type, (r, v) => r.Type = v),
			new ImportFieldBinding<UnitType>("base_type", r => ImportBaseTypes.Code(ImportBaseTypes.UnitTypeClasses, r.BaseType, 1),
				(r, v) => r.BaseType = ImportBaseTypes.Value(ImportBaseTypes.UnitTypeClasses, v, 1))
		};

		public UnitTypesImportWriter(IUnitsService units, ICustomStateService customStates)
		{
			_units = units;
			_customStates = customStates;
		}

		public override string EntityKey => "unit-types";
		public override ImportEffects AffectedEffects => ImportEffects.UnitsCache | ImportEffects.AppsResync;
		protected override IReadOnlyList<ImportFieldBinding<UnitType>> Bindings => Fields;

		protected override async Task<List<UnitType>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _units.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>()).Where(t => t.DepartmentId == departmentId).ToList();

		protected override string KeyOf(UnitType record) => ImportCanonical.Int(record.UnitTypeId);
		protected override string NaturalKeyOf(UnitType record) => record.Type;
		protected override UnitType New(ImportWriteContext context, ImportCanonicalRow row) => new UnitType { DepartmentId = context.DepartmentId };

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, UnitType record, ImportCanonicalRow row, CancellationToken cancellationToken)
		{
			var token = row.Text("status_set");
			if (string.IsNullOrWhiteSpace(token) || ImportReferenceToken.Is(token, ImportReferenceToken.Label))
				return null;

			record.CustomStatesId = await CustomStatusesImportWriter.ResolveSetIdAsync(context, _customStates, token, cancellationToken);
			return record.CustomStatesId == null ? "IMP-REF-UNRESOLVED" : null;
		}

		protected override Task<UnitType> SaveAsync(ImportWriteContext context, UnitType record, CancellationToken cancellationToken) =>
			_units.SaveUnitTypeAsync(record, cancellationToken);

		protected override async Task ExportReferencesAsync(int departmentId, UnitType record, Dictionary<string, string> row, CancellationToken cancellationToken) =>
			row["status_set"] = record.CustomStatesId.HasValue ? (await _customStates.GetCustomSateByIdAsync(record.CustomStatesId.Value))?.Name : null;

		protected override async Task<bool> HasOutsideActivityAsync(ImportWriteContext context, UnitType record, CancellationToken cancellationToken) =>
			(await _units.GetAllUnitsForTypeAsync(context.DepartmentId, record.Type))?.Any(u => !u.IsDeleted) == true;

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, UnitType record, CancellationToken cancellationToken) =>
			await _units.DeleteUnitTypeAsync(record.UnitTypeId, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
	}
}
