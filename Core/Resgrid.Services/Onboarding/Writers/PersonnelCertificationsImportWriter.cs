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
	/// A person's certifications ("personnel-certifications"). Never matched automatically (the same person can hold two
	/// records of one type); re-imports are idempotent through the external-id map. Records already past their expiry are
	/// imported Expired by default, so the expiry sweep has nothing to announce (L8, section 11.2.3); holders are dormant
	/// until activation, so expiring-soon notices wait for them.
	/// </summary>
	public sealed class PersonnelCertificationsImportWriter : ImportRecordWriter<PersonnelCertification>
	{
		private readonly ICertificationService _certifications;

		private static readonly IReadOnlyList<ImportFieldBinding<PersonnelCertification>> Fields = new[]
		{
			new ImportFieldBinding<PersonnelCertification>("number", r => r.Number, (r, v) => r.Number = v),
			new ImportFieldBinding<PersonnelCertification>("area", r => r.Area, (r, v) => r.Area = v),
			new ImportFieldBinding<PersonnelCertification>("issued_by", r => r.IssuedBy, (r, v) => r.IssuedBy = v),
			new ImportFieldBinding<PersonnelCertification>("issued_on", r => ImportCanonical.Date(r.RecievedOn), (r, v) => r.RecievedOn = ParseDate(v)),
			new ImportFieldBinding<PersonnelCertification>("expires_on", r => ImportCanonical.Date(r.ExpiresOn), (r, v) => r.ExpiresOn = ParseDate(v))
		};

		public PersonnelCertificationsImportWriter(ICertificationService certifications)
		{
			_certifications = certifications;
		}

		public override string EntityKey => "personnel-certifications";
		public override ImportEffects AffectedEffects => ImportEffects.SearchRebuild;
		protected override IReadOnlyList<ImportFieldBinding<PersonnelCertification>> Bindings => Fields;

		private static DateTime? ParseDate(string value) =>
			DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;

		protected override async Task<List<PersonnelCertification>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _certifications.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>())
				.Where(c => c.DepartmentId == departmentId && !c.IsDeleted).ToList();

		protected override async Task<PersonnelCertification> GetAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var id = ImportCanonical.ToNullableInt(key);
			if (id == null)
				return null;
			var record = await _certifications.GetCertificationByIdAsync(id.Value);
			return record != null && record.DepartmentId == context.DepartmentId && !record.IsDeleted ? record : null;
		}

		protected override string KeyOf(PersonnelCertification record) => ImportCanonical.Int(record.PersonnelCertificationId);
		protected override string NaturalKeyOf(PersonnelCertification record) => null;

		// No automatic match: a certification row always creates unless the external-id map knows it (section 12.2).
		protected override string MatchKeyOf(PersonnelCertification record) => null;
		protected override string MatchKeyOf(ImportCanonicalRow row) => null;

		protected override PersonnelCertification New(ImportWriteContext context, ImportCanonicalRow row) => new PersonnelCertification
		{
			DepartmentId = context.DepartmentId,
			Status = (int)PersonnelCertificationStatuses.Active
		};

		public override async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var result = await base.PlanAsync(context, rows, cancellationToken);
			var today = DateTime.UtcNow.Date;
			var settings = await _certifications.GetCertificationSettingsAsync(context.DepartmentId);
			var leadDays = settings?.GetNotifyLeadDays() ?? Array.Empty<int>();
			var horizon = today.AddDays(leadDays.Count > 0 ? leadDays.Max() : 60);
			var creating = new HashSet<string>(result.Rows.Where(r => r.Decision == ImportRowDecision.Create).Select(r => r.StagedRowId), StringComparer.Ordinal);

			var expired = rows.Count(r => creating.Contains(r.StagedRowId) && r.Date("expires_on") is DateTime e && e.Date < today);
			var expiring = rows.Count(r => creating.Contains(r.StagedRowId) && r.Date("expires_on") is DateTime e && e.Date >= today && e.Date <= horizon);
			if (expired > 0)
				result.Consequences["certifications.already-expired"] = expired;
			if (expiring > 0)
				result.Consequences["certifications.expiring-soon"] = expiring;
			return result;
		}

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, PersonnelCertification record, ImportCanonicalRow row,
			CancellationToken cancellationToken)
		{
			var person = row.Text("person");
			if (string.IsNullOrWhiteSpace(person) || ImportReferenceToken.Is(person, ImportReferenceToken.Label))
				return "IMP-REF-UNRESOLVED";
			record.UserId = await context.ResolveReference("personnel", person, cancellationToken);
			if (string.IsNullOrWhiteSpace(record.UserId))
				return "IMP-REF-UNRESOLVED";

			record.DepartmentCertificationTypeId = await ResolveIntAsync(context, "certification-types", row.Text("certification_type"), cancellationToken);
			if (record.DepartmentCertificationTypeId == null)
				return "IMP-REF-UNRESOLVED";

			// Already past its expiry: Expired now, so the sweep does not announce it on the first night (default L8 choice).
			if (record.ExpiresOn.HasValue && record.ExpiresOn.Value.Date < DateTime.UtcNow.Date && context.Consequences.SuppressExpiredCertificationNotices)
			{
				record.Status = (int)PersonnelCertificationStatuses.Expired;
				record.StatusChangedOn = DateTime.UtcNow;
				record.StatusChangedByUserId = context.ActingUserId;
				record.StatusReason = "imported-expired";
			}

			return null;
		}

		protected override async Task<PersonnelCertification> SaveAsync(ImportWriteContext context, PersonnelCertification record, CancellationToken cancellationToken)
		{
			try
			{
				return await _certifications.SaveCertificationAsync(record, context.Effects, cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message == "certifications_type_scope" || ex.Message == "certifications_type_not_found")
			{
				// A unit-only type, or one deleted since preview: the row fails, the chunk continues (row-by-row replay).
				throw new ImportRowRejectedException(ex.Message == "certifications_type_scope" ? "IMP-CERT-TYPE-SCOPE" : "IMP-TARGET-MISSING");
			}
		}

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, PersonnelCertification record, CancellationToken cancellationToken) =>
			await _certifications.DeleteCertification(record, cancellationToken) ? ImportRollbackOutcome.Deleted : ImportRollbackOutcome.ManualReconciliation;
	}
}
