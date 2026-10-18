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
	/// Which certifications a role requires ("role-requirements", wizard plan section 8.1 B). One row is one role and one
	/// certification type; an existing requirement for the same pair is matched (and, in a reviewed update, its options
	/// changed). The service saves a role's whole list, so each write keeps the role's other requirements as they are. When
	/// the department enforces requirements, the preview says so: people lacking a newly required certification can lose the
	/// role at the next nightly check.
	/// </summary>
	public sealed class RoleRequirementsImportWriter : ImportRecordWriter<PersonnelRoleCertificationRequirement>
	{
		public const string EnforcedConsequence = "role-requirements.enforced";

		private readonly ICertificationService _certifications;

		private static readonly IReadOnlyList<ImportFieldBinding<PersonnelRoleCertificationRequirement>> Fields = new[]
		{
			new ImportFieldBinding<PersonnelRoleCertificationRequirement>("mandatory", r => ImportCanonical.Bool(r.IsMandatory), (r, v) => r.IsMandatory = v == null || ImportCanonical.ToBool(v)),
			new ImportFieldBinding<PersonnelRoleCertificationRequirement>("any_of_group", r => ImportCanonical.Int(r.AnyOfGroup), (r, v) => r.AnyOfGroup = ImportCanonical.ToNullableInt(v)),
			new ImportFieldBinding<PersonnelRoleCertificationRequirement>("allow_trainee", r => ImportCanonical.Bool(r.AllowTrainee), (r, v) => r.AllowTrainee = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<PersonnelRoleCertificationRequirement>("grace_days", r => ImportCanonical.Int(r.GraceDaysOverride), (r, v) => r.GraceDaysOverride = ImportCanonical.ToNullableInt(v))
		};

		public RoleRequirementsImportWriter(ICertificationService certifications)
		{
			_certifications = certifications;
		}

		public override string EntityKey => "role-requirements";
		public override ImportEffects AffectedEffects => ImportEffects.RoleVisibility;
		protected override IReadOnlyList<ImportFieldBinding<PersonnelRoleCertificationRequirement>> Bindings => Fields;

		protected override async Task<List<PersonnelRoleCertificationRequirement>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			(await _certifications.GetAllRoleRequirementsAsync(departmentId) ?? new List<PersonnelRoleCertificationRequirement>())
				.Where(r => r.DepartmentId == departmentId).ToList();

		protected override string KeyOf(PersonnelRoleCertificationRequirement record) => ImportCanonical.Int(record.PersonnelRoleCertificationRequirementId);
		protected override string NaturalKeyOf(PersonnelRoleCertificationRequirement record) => null;

		/// <summary>A requirement is its role and type; a row matches only when both name existing records.</summary>
		protected override string MatchKeyOf(PersonnelRoleCertificationRequirement record) =>
			ImportCanonical.Int(record.PersonnelRoleId) + "|" + ImportCanonical.Int(record.DepartmentCertificationTypeId);

		protected override string MatchKeyOf(ImportCanonicalRow row)
		{
			var role = row.Text("role");
			var type = row.Text("certification_type");
			return ImportReferenceToken.Is(role, ImportReferenceToken.Existing) && ImportReferenceToken.Is(type, ImportReferenceToken.Existing)
				? ImportReferenceToken.Value(role, ImportReferenceToken.Existing) + "|" + ImportReferenceToken.Value(type, ImportReferenceToken.Existing)
				: null;
		}

		protected override PersonnelRoleCertificationRequirement New(ImportWriteContext context, ImportCanonicalRow row) => new PersonnelRoleCertificationRequirement
		{
			DepartmentId = context.DepartmentId,
			IsMandatory = true
		};

		public override async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var result = await base.PlanAsync(context, rows, cancellationToken);
			var adding = result.Rows.Count(r => r.Decision == ImportRowDecision.Create || r.Decision == ImportRowDecision.Update);
			var settings = await _certifications.GetCertificationSettingsAsync(context.DepartmentId);
			if (adding > 0 && settings?.EnforcementMode == (int)CertificationEnforcementModes.Enforce)
				result.Consequences[EnforcedConsequence] = adding;
			return result;
		}

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, PersonnelRoleCertificationRequirement record, ImportCanonicalRow row,
			CancellationToken cancellationToken)
		{
			var roleId = await ResolveIntAsync(context, "roles", row.Text("role"), cancellationToken);
			var typeId = await ResolveIntAsync(context, "certification-types", row.Text("certification_type"), cancellationToken);
			if (roleId == null || typeId == null)
				return "IMP-REF-UNRESOLVED";
			record.PersonnelRoleId = roleId.Value;
			record.DepartmentCertificationTypeId = typeId.Value;
			return null;
		}

		/// <summary>Writes the role's whole list with this requirement added or changed; the role's other requirements stay.</summary>
		protected override async Task<PersonnelRoleCertificationRequirement> SaveAsync(ImportWriteContext context, PersonnelRoleCertificationRequirement record,
			CancellationToken cancellationToken)
		{
			var list = (await _certifications.GetRoleRequirementsAsync(record.PersonnelRoleId) ?? new List<PersonnelRoleCertificationRequirement>())
				.Where(r => r.DepartmentCertificationTypeId != record.DepartmentCertificationTypeId).ToList();
			list.Add(record);
			try
			{
				var saved = await _certifications.SaveRoleRequirementsAsync(context.DepartmentId, record.PersonnelRoleId, list, context.ActingUserId, cancellationToken);
				return saved.First(r => r.DepartmentCertificationTypeId == record.DepartmentCertificationTypeId);
			}
			catch (InvalidOperationException ex) when (ex.Message == "certifications_type_scope" || ex.Message == "certifications_type_not_found" ||
				ex.Message == "certifications_role_not_found" || ex.Message == "certifications_grace_invalid")
			{
				throw new ImportRowRejectedException(ex.Message switch
				{
					"certifications_type_scope" => "IMP-REQUIREMENT-TYPE-SCOPE",
					"certifications_grace_invalid" => "IMP-TYPE-RANGE",
					_ => "IMP-TARGET-MISSING"
				});
			}
		}

		/// <summary>Removes the requirement the import added; the role's other requirements stay.</summary>
		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, PersonnelRoleCertificationRequirement record,
			CancellationToken cancellationToken)
		{
			var list = (await _certifications.GetRoleRequirementsAsync(record.PersonnelRoleId) ?? new List<PersonnelRoleCertificationRequirement>())
				.Where(r => r.PersonnelRoleCertificationRequirementId != record.PersonnelRoleCertificationRequirementId).ToList();
			await _certifications.SaveRoleRequirementsAsync(context.DepartmentId, record.PersonnelRoleId, list, context.ActingUserId, cancellationToken);
			return ImportRollbackOutcome.Deleted;
		}
	}
}
