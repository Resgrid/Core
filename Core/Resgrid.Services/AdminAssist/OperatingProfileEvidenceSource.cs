using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Only validated public archetype codes enter the setup projection; references and declared system labels stay on the editor.</summary>
	public sealed class OperatingProfileEvidenceSource(IDepartmentSettingsService settings) : IAdminAssistEvidenceSource
	{
		public string SourceId => "OperatingProfile";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "operatingPackIds", "operatingProfileReviewed" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var profile = await settings.GetOperatingProfileAsync(actor.DepartmentId).WaitAsync(ct) ?? throw new InvalidOperationException();
			Validator.ValidateObject(profile, new ValidationContext(profile), true);
			return new[] {
				new ConfigurationEvidence("operatingPackIds", EvidenceState.Known, SourceId, profile.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), now, Code: string.Join(",", profile.Archetypes.OrderBy(a => a, StringComparer.Ordinal))),
				new ConfigurationEvidence("operatingProfileReviewed", EvidenceState.Known, SourceId, "1", now, Boolean: profile.ReviewedOnUtc.HasValue)
			};
		}
	}
}
