using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Metadata existence/expiry checks only. Referenced content is neither retrieved nor certified.</summary>
	public sealed class AdministrativeReferenceEvidenceSource(IDepartmentSettingsService settings, IAdministrativeReferenceStore references) : IAdminAssistEvidenceSource
	{
		public string SourceId => "AdministrativeReferences";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "declaredPolicyReferences", "unavailablePolicyReferences", "policyReferencesExpiring30Days", "declaredSiteReferences", "unavailableSiteReferences", "declaredContinuityReferences" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var profile = await settings.GetOperatingProfileAsync(actor.DepartmentId).WaitAsync(ct) ?? throw new InvalidOperationException();
			Validator.ValidateObject(profile, new ValidationContext(profile), true);
			int[] Parse(IEnumerable<string> values) => values.Select(v => int.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture)).Distinct().ToArray();
			var documents = Parse(profile.StaffingPolicyReferences.Concat(profile.QualificationPolicyReferences).Concat(profile.ContinuityProcedureReferences));
			var groups = Parse(profile.SiteGroupReferences);
			var result = await references.ReadAdministrativeReferencesAsync(actor.DepartmentId, documents, groups, now, ct) ?? throw new InvalidOperationException();
			if (result != await references.ReadAdministrativeReferencesAsync(actor.DepartmentId, documents, groups, now, ct)) throw new InvalidOperationException("Reference evidence changed.");
			ConfigurationEvidence Count(string id, int value) => new(id, EvidenceState.Known, SourceId, "references-v1", now, Number: value);
			return new[] { Count("declaredPolicyReferences", result.PolicyReferences), Count("unavailablePolicyReferences", result.UnavailablePolicies),
				Count("policyReferencesExpiring30Days", result.ExpiringPolicies), Count("declaredSiteReferences", result.SiteReferences),
				Count("unavailableSiteReferences", result.UnavailableSites), Count("declaredContinuityReferences", profile.ContinuityProcedureReferences.Distinct().Count()) };
		}
	}
}
