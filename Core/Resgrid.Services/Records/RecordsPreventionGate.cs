using System;
using System.Data.SqlTypes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The checks every RMS-5 module service repeats (RMS plan section 6, RMS-5: "each module includes permissions,
	/// audit ..."): the module flag on top of the Records cutover, the viewer / prevention-administrator /
	/// restricted-viewer permissions, the per-department number sequences and the audit row. One class so a module
	/// cannot ship a weaker gate than its siblings.
	/// </summary>
	public sealed class RecordsPreventionGate
	{
		private readonly IRecordsCutoverService _cutover;
		private readonly IFeatureToggleService _flags;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsPreventionSequencesRepository _sequences;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IDocumentNumberingService _numbering;

		public RecordsPreventionGate(IRecordsCutoverService cutover, IFeatureToggleService flags, IRecordsAuthorizationService authorization,
			IRmsPreventionSequencesRepository sequences, IRmsAccessAuditsRepository audits, IDocumentNumberingService numbering)
		{
			_numbering = numbering;
			_cutover = cutover;
			_flags = flags;
			_authorization = authorization;
			_sequences = sequences;
			_audits = audits;
		}

		/// <summary>A module is usable only when Records itself is usable for the department and the module flag is on.</summary>
		public async Task<bool> IsEnabledAsync(int departmentId, RecordsPreventionModule module)
		{
			var state = await _cutover.GetModuleStateAsync(departmentId);
			if (state == null || !state.RecordsUsable)
				return false;
			return await _flags.IsEnabledAsync(RecordsPreventionModules.FlagKey(module), departmentId);
		}

		public async Task RequireEnabledAsync(int departmentId, RecordsPreventionModule module)
		{
			if (!await IsEnabledAsync(departmentId, module))
				throw new RecordsModuleDisabledException(module);
		}

		/// <summary>Any active department member with Records access may read prevention data.</summary>
		public async Task RequireViewerAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId) || !await _authorization.IsActiveMemberAsync(userId, departmentId))
				throw new UnauthorizedAccessException("Records access is required.");
		}

		/// <summary>Whether prevention or investigations work (an inspector, an evidence custodian) may be handed to this person: not removed, disabled, hidden or foreign.</summary>
		public async Task<bool> IsAssignableAsync(int departmentId, string userId)
			=> !string.IsNullOrWhiteSpace(userId) && await _authorization.IsAssignableMemberAsync(userId, departmentId);

		/// <summary>Changing prevention data needs the PreventionAdmin permission (registry value 69; department admins by default).</summary>
		public async Task RequireAdminAsync(int departmentId, string userId)
		{
			await RequireViewerAsync(departmentId, userId);
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.RecordsPreventionAdmin))
				throw new UnauthorizedAccessException("The prevention administrator permission is required.");
		}

		/// <summary>Investigations sit behind RecordRestricted_View (value 59); case membership is checked on top by the investigations service.</summary>
		public async Task RequireRestrictedAsync(int departmentId, string userId)
		{
			await RequireViewerAsync(departmentId, userId);
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords))
				throw new UnauthorizedAccessException("Restricted Records access is required.");
		}

		public Task<bool> HasAdminAsync(int departmentId, string userId)
			=> _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.RecordsPreventionAdmin);

		public Task<bool> IsDepartmentAdminAsync(int departmentId, string userId)
			=> _authorization.IsDepartmentAdminAsync(userId, departmentId);

		/// <summary>
		/// The department's own pattern for the kind when it set one (setting 72's DocumentPatterns), otherwise the built-in
		/// {KIND}-{year}-{sequence:0000}, allocated atomically per department/kind/year. The year is the records numbering year
		/// (department-local, a fiscal year when setting 72 starts one); the counter is keyed by that name, so a year start change
		/// whose name matches a year already counted carries on rather than issuing its numbers again.
		/// </summary>
		public async Task<string> NextNumberAsync(int departmentId, string kind, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var documentKind = DocumentNumberKinds.ForPreventionKind(kind);
			var custom = documentKind == null ? null : await _numbering.TakeCustomNumberAsync(departmentId, documentKind, utcNow, cancellationToken);
			if (custom != null)
				return custom;

			var year = await _numbering.GetNumberingYearAsync(departmentId, documentKind ?? DocumentNumberKinds.Occupancy, utcNow);
			var next = await _sequences.NextAsync(departmentId, kind, year, cancellationToken);
			return $"{kind}-{year}-{next:0000}";
		}

		/// <summary>
		/// Writes an audit row for a prevention or investigation aggregate. RecordId stays null because these are not
		/// Records; the aggregate id rides CorrelationId so the audit trail of a case can be read back without the
		/// repository's live-record check rewriting the row.
		/// </summary>
		public Task AuditAsync(int departmentId, string userId, RmsAccessAuditAction action, string purpose, string aggregateId, object detail,
			bool successful = true, string ipAddress = null, CancellationToken cancellationToken = default)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = null,
				ActorUserId = userId,
				Action = (int)action,
				Successful = successful,
				OccurredOn = DateTime.UtcNow,
				Purpose = Trim(purpose, 100),
				CorrelationId = Trim(aggregateId, 36),
				IpAddress = Trim(ipAddress, 64),
				OriginClient = (int)RmsOriginClient.Web,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}

		public static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
		}

		public static string Require(string value, int max, string message)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException(message);
			var trimmed = value.Trim();
			if (trimmed.Length > max)
				throw new ArgumentException($"{message} (at most {max} characters).");
			return trimmed;
		}

		/// <summary>
		/// Dapper binds DateTime as SQL datetime, which starts at 1753, so a client date outside it (a two-digit year sent as
		/// 0026) failed the whole statement with SqlDateTime overflow. Refuse it here so the web and API both get a message.
		/// </summary>
		public static DateTime RequireStorableDate(DateTime value, string message)
		{
			if (value < (DateTime)SqlDateTime.MinValue || value > (DateTime)SqlDateTime.MaxValue)
				throw new ArgumentException(message);
			return value;
		}

		public static DateTime? RequireStorableDate(DateTime? value, string message) => value.HasValue ? RequireStorableDate(value.Value, message) : null;
	}
}
