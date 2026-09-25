using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Distinguishes a known unavailable optional capability from an availability-read failure.</summary>
	public sealed class CapabilityEvidenceSource(IAdminAssistAccessService access, IAdminAssistCatalog catalog) : IAdminAssistEvidenceSource
	{
		public string SourceId => "CapabilityAvailability";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "maintenanceAvailable" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var capability = catalog.Capabilities.First(c => c.Location.Controller == "WorkOrders");
			var result = await access.GetCapabilityAsync(actor, capability.Id, ct) ?? throw new InvalidOperationException();
			var unavailable = result.State == EvidenceState.Unavailable && result.ReasonCodes.Any(r =>
				r == "FeatureNotEnabled" || r == "ModuleDisabled" || r == "AddonRequired.ReadinessPro");
			var known = result.State == EvidenceState.Known || unavailable;
			return new[] { new ConfigurationEvidence("maintenanceAvailable", known ? EvidenceState.Known : EvidenceState.Unknown,
				SourceId, catalog.Version, now, Boolean: known ? result.State == EvidenceState.Known : null, ReasonCode: known ? null : "AvailabilityUnknown") };
		}
	}
}
