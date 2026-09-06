using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Shared reveal logic for the Web MVC and v4 endpoints (RMS plan section 5.9.3). The aggregate is already
	/// hydrated through the seam with the caller's grant; this keys the cataloged columns the way the page markers
	/// expect ("{table}.{column}:{rowId}"), withholds restricted columns without RecordRestricted_View, and audits.
	/// </summary>
	public class RecordsRevealService : IRecordsRevealService
	{
		private readonly IRecordsService _records;
		private readonly IIncidentReportsService _incidents;

		public RecordsRevealService(IRecordsService records, IIncidentReportsService incidents)
		{
			_records = records;
			_incidents = incidents;
		}

		public async Task<RecordRevealResult> RevealRecordAsync(int departmentId, string userId, RecordAggregate aggregate, bool canViewRestricted, string ipAddress)
		{
			if (aggregate?.Record == null) throw new ArgumentNullException(nameof(aggregate));
			var protection = aggregate.Protection ?? new ProtectedReadResult();
			if (protection.IsProtected && protection.ProtectedReason != null)
				return new RecordRevealResult { Success = false, Error = protection.ProtectedReason };

			var fields = new Dictionary<string, string>();
			var details = aggregate.Details;
			if (details != null)
			{
				foreach (var accessor in RmsProtectedFields.Details)
				{
					// The reveal hides exactly what the page hides: restricted detail columns stay withheld without the grant.
					var column = accessor.Key.Substring(accessor.Key.IndexOf('.') + 1);
					if (!canViewRestricted && RecordSnapshotSerializer.RestrictedDetailFields.Any(f => string.Equals(f, column, StringComparison.OrdinalIgnoreCase)))
						continue;
					fields[$"{accessor.Key}:{details.RmsOperationalRecordDetailId}"] = accessor.Value.Get(details);
				}
			}
			foreach (var attachment in aggregate.Attachments ?? new List<RmsRecordAttachment>())
				fields[$"rmsrecordattachments.filename:{attachment.RmsRecordAttachmentId}"] = attachment.FileName;

			// Department-definition values are not cataloged yet (catalog v11); withheld cells stay withheld here too.
			await _records.RecordAccessAsync(departmentId, userId, aggregate.Record.RmsOperationalRecordId, null, RmsAccessAuditAction.Read, "Protected reveal", ipAddress);
			return new RecordRevealResult { Success = true, Fields = fields };
		}

		public async Task<RecordRevealResult> RevealIncidentAsync(int departmentId, string userId, IncidentReportAggregate aggregate, bool canViewRestricted, string ipAddress)
		{
			if (aggregate?.Report == null) throw new ArgumentNullException(nameof(aggregate));
			var protection = aggregate.Protection ?? new ProtectedReadResult();
			if (protection.IsProtected && protection.ProtectedReason != null)
				return new RecordRevealResult { Success = false, Error = protection.ProtectedReason };

			var fields = new Dictionary<string, string>();
			void Add<T>(IEnumerable<T> rows, Func<T, string> key, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors)
			{
				foreach (var row in rows ?? Enumerable.Empty<T>())
					foreach (var accessor in accessors)
						fields[$"{accessor.Key}:{key(row)}"] = accessor.Value.Get(row);
			}
			if (aggregate.Narrative != null) Add(new[] { aggregate.Narrative }, n => n.RmsNarrativeId, RmsProtectedFields.Narratives);
			if (aggregate.Location != null)
			{
				Add(new[] { aggregate.Location }, l => l.RmsLocationId, RmsProtectedFields.Locations);
				fields[$"rmslocations.coordinates:{aggregate.Location.RmsLocationId}"] = aggregate.Location.Latitude.HasValue ? aggregate.Location.Latitude + ", " + aggregate.Location.Longitude : "-";
			}
			Add(aggregate.Facts, f => f.RmsSourceFactId, RmsProtectedFields.SourceFacts);
			Add(aggregate.Exposures, e => e.RmsExposureId, RmsProtectedFields.Exposures);
			Add(aggregate.Resources, r => r.RmsIncidentResourceId, RmsProtectedFields.Resources);
			Add(aggregate.Modules, m => m.RmsIncidentModuleId, RmsProtectedFields.Modules);
			if (canViewRestricted)
				Add(aggregate.Casualties, c => c.RmsCasualtyRescueId, RmsProtectedFields.Casualties);
			foreach (var attachment in aggregate.Attachments ?? new List<RmsRecordAttachment>())
				fields[$"rmsrecordattachments.filename:{attachment.RmsRecordAttachmentId}"] = attachment.FileName;

			await _incidents.RecordAccessAsync(departmentId, userId, aggregate.Report.RmsIncidentReportId, null, RmsAccessAuditAction.Read, "Protected reveal", ipAddress);
			return new RecordRevealResult { Success = true, Fields = fields };
		}
	}
}
