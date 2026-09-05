using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Source-authorized choices for both officer evidence forms. Selection never captures content.</summary>
	public class RecordEvidenceSelectionService : IRecordEvidenceSelectionService
	{
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidents;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsEvidenceService _evidence;
		private readonly ICallsService _calls;
		private readonly IUnitsService _units;
		private readonly IDepartmentsService _departments;
		private readonly Lazy<IAuthorizationService> _sourceAuthorization;
		private readonly IChatChannelRepository _channels;
		private readonly IChatMessageRepository _messages;
		private readonly Lazy<IChatPermissionService> _chat;

		public RecordEvidenceSelectionService(IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository incidents,
			IRecordsAuthorizationService authorization, IRecordsCutoverService cutover, IRecordsEvidenceService evidence,
			ICallsService calls, IUnitsService units, IDepartmentsService departments, Lazy<IAuthorizationService> sourceAuthorization,
			IChatChannelRepository channels, IChatMessageRepository messages, Lazy<IChatPermissionService> chat)
		{
			_records = records; _incidents = incidents; _authorization = authorization; _cutover = cutover;
			_evidence = evidence; _calls = calls; _units = units; _departments = departments;
			_sourceAuthorization = sourceAuthorization; _channels = channels; _messages = messages; _chat = chat;
		}

		public async Task<RecordEvidenceContext> GetContextAsync(int departmentId, string userId, string recordId, RmsRecordKind recordKind)
		{
			if (string.IsNullOrWhiteSpace(userId) || !(await _cutover.GetModuleStateAsync(departmentId)).RecordsUsable ||
				!await _authorization.CanUserViewRecordAsync(userId, recordId, departmentId)) throw new UnauthorizedAccessException();
			var context = new RecordEvidenceContext { RecordId = recordId, RecordKind = recordKind };
			string author, owner, amendment; int state;
			if (recordKind == RmsRecordKind.Operational)
			{
				var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
				if (record == null || record.DepartmentId != departmentId || record.DeletedOn.HasValue || record.PurgedOn.HasValue) throw new UnauthorizedAccessException();
				context.RecordNumber = record.RecordNumber ?? record.DraftReference; context.RowVersion = record.RowVersion;
				context.CallId = record.CallId; context.StartUtc = record.StartedOn; context.EndUtc = record.EndedOn;
				author = record.AuthorUserId; owner = record.OwnerUserId; amendment = record.AmendsRevisionId; state = record.State;
			}
			else if (recordKind == RmsRecordKind.IncidentReport)
			{
				var report = await _incidents.GetByIdForDepartmentAsync(departmentId, recordId);
				if (report == null || report.DepartmentId != departmentId || report.DeletedOn.HasValue || report.PurgedOn.HasValue) throw new UnauthorizedAccessException();
				context.RecordNumber = report.RecordNumber ?? report.DraftReference; context.RowVersion = report.RowVersion;
				context.CallId = report.CallId; context.StartUtc = report.CallCreatedOn; context.EndUtc = report.IncidentClearedOn;
				author = report.AuthorUserId; owner = report.OwnerUserId; amendment = report.AmendsRevisionId; state = report.State;
			}
			else throw new ArgumentException("Choose an operational record or incident report.");
			context.CanCapture = !RmsLifecycle.IsTerminal((RmsRecordState)state) && (RmsLifecycle.IsEditable((RmsRecordState)state) || amendment != null)
				&& await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord)
				&& (author == userId || owner == userId || await _authorization.IsDepartmentAdminAsync(userId, departmentId)
					|| (amendment != null && await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.AmendRecords)));
			context.CanViewRestricted = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			context.CanExport = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ExportRecords);
			if (!await _authorization.CanUserViewRecordAsync(userId, recordId, departmentId)) throw new UnauthorizedAccessException();
			return context;
		}

		public async Task<RecordEvidenceSelection> GetAsync(int departmentId, string userId, string recordId, RmsRecordKind recordKind,
			RmsEvidenceKind sourceKind, string channelId = null, long afterSequence = 0)
		{
			if (!Enum.IsDefined(typeof(RmsEvidenceKind), sourceKind) || afterSequence < 0) throw new ArgumentException("Choose a supported evidence source and page.");
			var context = await GetContextAsync(departmentId, userId, recordId, recordKind);
			if (!context.CanCapture) throw new UnauthorizedAccessException();
			var selection = new RecordEvidenceSelection { Context = context, SourceKind = sourceKind, ChannelId = channelId,
				Sources = await _evidence.GetSourceStatesAsync(departmentId) };
			await RequireSourceCallAsync(context, departmentId, userId);
			var source = selection.Sources.FirstOrDefault(s => s.Kind == sourceKind);
			if (source?.Available == true)
			{
				if (Restricted(sourceKind) && !context.CanViewRestricted) throw new UnauthorizedAccessException();
				if (sourceKind == RmsEvidenceKind.TrackingFix)
				{
					foreach (var unit in (await _units.GetUnitsForDepartmentAsync(departmentId)).Where(u => u.DepartmentId == departmentId))
						if (await _sourceAuthorization.Value.CanUserViewUnitLocationAsync(userId, unit.UnitId, departmentId))
							selection.Choices.Add(new RecordEvidenceChoice { Id = unit.UnitId.ToString(CultureInfo.InvariantCulture), Label = unit.Name });
				}
				else if (sourceKind == RmsEvidenceKind.CertificationSnapshot)
				{
					foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId))
						if (await _sourceAuthorization.Value.CanUserViewPersonAsync(userId, person.UserId, departmentId))
							selection.Choices.Add(new RecordEvidenceChoice { Id = person.UserId, Label = person.Name });
				}
				else if (sourceKind == RmsEvidenceKind.ChatPromotion && context.CallId.HasValue)
				{
					foreach (var channel in (await _channels.GetByCallIdAsync(context.CallId.Value) ?? Enumerable.Empty<ChatChannel>())
						.Where(c => c.DepartmentId == departmentId && c.CallId == context.CallId))
						if (await _chat.Value.CanAccessChannelAsync(channel, userId, null))
							selection.Channels.Add(new RecordEvidenceChoice { Id = channel.ChatChannelId, Label = channel.Name });
					if (!string.IsNullOrWhiteSpace(channelId))
					{
						if (!selection.Channels.Any(c => c.Id == channelId)) throw new UnauthorizedAccessException();
						// Includes thread replies. Advance over withheld rows too, so a deleted page cannot trap the officer.
						var rows = (await _messages.GetAfterSeqAsync(channelId, afterSequence, 101) ?? Enumerable.Empty<ChatMessage>()).ToList();
						var page = rows.Take(100).ToList();
						if (rows.Count > 100 && page.Count > 0) selection.NextSequence = page.Max(m => m.MessageSeq);
						foreach (var message in page.Where(m => m.DepartmentId == departmentId && m.ChatChannelId == channelId && !m.DeletedOn.HasValue && !m.IsModerated))
							selection.Choices.Add(new RecordEvidenceChoice { Id = message.ChatMessageId, Label = message.SenderDisplayName,
								Body = message.Body, OccurredOn = message.SentOn, EditedOn = message.EditedOn, Sequence = message.MessageSeq });
					}
				}
				else if (sourceKind == RmsEvidenceKind.InventoryUsage && !await _authorization.CanUseSourceInventoryAsync(userId, departmentId, null))
					throw new UnauthorizedAccessException();
			}

			// Recheck earlier choices after the last awaited source read. No caller-provided IDs grant access.
			foreach (var choice in selection.Choices)
			{
				if (sourceKind == RmsEvidenceKind.TrackingFix && !await _sourceAuthorization.Value.CanUserViewUnitLocationAsync(userId, int.Parse(choice.Id, CultureInfo.InvariantCulture), departmentId)) throw new UnauthorizedAccessException();
				if (sourceKind == RmsEvidenceKind.CertificationSnapshot && !await _sourceAuthorization.Value.CanUserViewPersonAsync(userId, choice.Id, departmentId)) throw new UnauthorizedAccessException();
			}
			if (selection.Channels.Count > 0)
			{
				var current = (await _channels.GetByCallIdAsync(context.CallId.Value) ?? Enumerable.Empty<ChatChannel>()).ToList();
				foreach (var item in selection.Channels)
				{
					var channel = current.FirstOrDefault(c => c.ChatChannelId == item.Id && c.DepartmentId == departmentId && c.CallId == context.CallId);
					if (channel == null || !await _chat.Value.CanAccessChannelAsync(channel, userId, null)) throw new UnauthorizedAccessException();
				}
			}
			await RequireSourceCallAsync(context, departmentId, userId);
			var final = await GetContextAsync(departmentId, userId, recordId, recordKind);
			if (!final.CanCapture || (Restricted(sourceKind) && !final.CanViewRestricted)) throw new UnauthorizedAccessException();
			if (context.RowVersion != final.RowVersion || context.CallId != final.CallId) throw new RecordConcurrencyException(recordId, context.RowVersion, final.RowVersion);
			return selection;
		}

		private async Task RequireSourceCallAsync(RecordEvidenceContext context, int departmentId, string userId)
		{
			if (context.CallId.HasValue && !await _authorization.CanReadSourceCallAsync(userId, departmentId, await _calls.GetCallByIdAsync(context.CallId.Value)))
				throw new UnauthorizedAccessException();
		}
		private static bool Restricted(RmsEvidenceKind kind) => kind is RmsEvidenceKind.ChatPromotion or RmsEvidenceKind.CertificationSnapshot or RmsEvidenceKind.InventoryUsage;
	}
}
