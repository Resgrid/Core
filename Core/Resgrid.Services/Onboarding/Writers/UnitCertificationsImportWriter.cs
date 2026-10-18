using System;
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
	/// A unit's certifications ("unit-certifications": inspections, registrations, pump tests…). Like a person's, never
	/// matched automatically (a unit can hold two records of one type); re-imports are idempotent through the external-id map.
	/// Records already past their expiry are imported Expired, so the expiry sweep has nothing to announce (acknowledged as a
	/// consequence). Saved through the quiet overload: no per-row audit or domain event, the engine reports the run.
	/// </summary>
	public sealed class UnitCertificationsImportWriter : ImportRecordWriter<UnitCertification>
	{
		public const string AlreadyExpiredConsequence = "unit-certifications.already-expired";

		private readonly ICertificationService _certifications;

		private static readonly IReadOnlyList<ImportFieldBinding<UnitCertification>> Fields = new[]
		{
			new ImportFieldBinding<UnitCertification>("number", r => r.Number, (r, v) => r.Number = v),
			new ImportFieldBinding<UnitCertification>("issued_by", r => r.IssuedBy, (r, v) => r.IssuedBy = v),
			new ImportFieldBinding<UnitCertification>("issued_on", r => ImportCanonical.Date(r.IssuedOn), (r, v) => r.IssuedOn = ParseDate(v)),
			new ImportFieldBinding<UnitCertification>("expires_on", r => ImportCanonical.Date(r.ExpiresOn), (r, v) => r.ExpiresOn = ParseDate(v)),
			new ImportFieldBinding<UnitCertification>("notes", r => r.Notes, (r, v) => r.Notes = v)
		};

		public UnitCertificationsImportWriter(ICertificationService certifications)
		{
			_certifications = certifications;
		}

		public override string EntityKey => "unit-certifications";
		public override ImportEffects AffectedEffects => ImportEffects.SearchRebuild;
		protected override IReadOnlyList<ImportFieldBinding<UnitCertification>> Bindings => Fields;

		private static DateTime? ParseDate(string value) =>
			DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;

		protected override async Task<List<UnitCertification>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _certifications.GetUnitCertificationsForDepartmentAsync(departmentId) ?? new List<UnitCertification>())
				.Where(c => c.DepartmentId == departmentId && !c.IsDeleted).ToList();

		protected override async Task<UnitCertification> GetAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var id = ImportCanonical.ToNullableInt(key);
			if (id == null)
				return null;
			var record = await _certifications.GetUnitCertificationByIdAsync(id.Value);
			return record != null && record.DepartmentId == context.DepartmentId && !record.IsDeleted ? record : null;
		}

		protected override string KeyOf(UnitCertification record) => ImportCanonical.Int(record.UnitCertificationId);
		protected override string NaturalKeyOf(UnitCertification record) => null;

		// No automatic match: a certification row always creates unless the external-id map knows it (section 12.2).
		protected override string MatchKeyOf(UnitCertification record) => null;
		protected override string MatchKeyOf(ImportCanonicalRow row) => null;

		protected override UnitCertification New(ImportWriteContext context, ImportCanonicalRow row) => new UnitCertification
		{
			DepartmentId = context.DepartmentId,
			Status = (int)UnitCertificationStatuses.Active
		};

		public override async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var result = await base.PlanAsync(context, rows, cancellationToken);
			var today = DateTime.UtcNow.Date;
			var creating = new HashSet<string>(result.Rows.Where(r => r.Decision == ImportRowDecision.Create).Select(r => r.StagedRowId), StringComparer.Ordinal);
			var expired = rows.Count(r => creating.Contains(r.StagedRowId) && r.Date("expires_on") is DateTime e && e.Date < today);
			if (expired > 0)
				result.Consequences[AlreadyExpiredConsequence] = expired;
			return result;
		}

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, UnitCertification record, ImportCanonicalRow row,
			CancellationToken cancellationToken)
		{
			var unitId = await ResolveIntAsync(context, "units", row.Text("unit"), cancellationToken);
			var typeId = await ResolveIntAsync(context, "certification-types", row.Text("certification_type"), cancellationToken);
			if (unitId == null || typeId == null)
				return "IMP-REF-UNRESOLVED";
			record.UnitId = unitId.Value;
			record.DepartmentCertificationTypeId = typeId.Value;

			// Already past its expiry: Expired now, so the sweep does not announce it on the first night.
			if (record.ExpiresOn.HasValue && record.ExpiresOn.Value.Date < DateTime.UtcNow.Date)
			{
				record.Status = (int)UnitCertificationStatuses.Expired;
				record.StatusChangedOn = DateTime.UtcNow;
				record.StatusChangedByUserId = context.ActingUserId;
				record.StatusReason = "imported-expired";
			}

			return null;
		}

		protected override async Task<UnitCertification> SaveAsync(ImportWriteContext context, UnitCertification record, CancellationToken cancellationToken)
		{
			try
			{
				return await _certifications.SaveUnitCertificationAsync(record, context.ActingUserId, context.Effects, cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == "certifications_type_scope" || ex.Message == "certifications_type_not_found" ||
				ex.Message == "certifications_unit_not_found")
			{
				// A person-only type, or a type or unit deleted since the preview: the row fails, the chunk continues.
				throw new ImportRowRejectedException(ex.Message == "certifications_type_scope" ? "IMP-UNIT-CERT-TYPE-SCOPE" : "IMP-TARGET-MISSING");
			}
		}

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, UnitCertification record, CancellationToken cancellationToken) =>
			await _certifications.DeleteUnitCertificationAsync(record.UnitCertificationId, context.DepartmentId, context.ActingUserId, cancellationToken)
				? ImportRollbackOutcome.Deleted
				: ImportRollbackOutcome.ManualReconciliation;
	}
}
