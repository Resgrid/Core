using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Author-targeted Records notifications (EventTypes 31). Message text is assembled from the record header
	/// only: type, reference, reviewer, reason code and the reviewer's note to the author, plus a link. No
	/// narrative or restricted-section content ever leaves through this path.
	/// </summary>
	public class RecordsNotificationService : IRecordsNotificationService
	{
		public const string ReturnedForCorrectionTitle = "Record returned for correction";
		private const int MaxReasonTextLength = 200;

		private readonly IRmsOperationalRecordsRepository _records;
		private readonly ICommunicationService _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IUserProfileService _profiles;

		public RecordsNotificationService(IRmsOperationalRecordsRepository records, ICommunicationService communication,
			IDepartmentsService departments, IDepartmentSettingsService departmentSettings, IUserProfileService profiles)
		{
			_records = records;
			_communication = communication;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_profiles = profiles;
		}

		public async Task<bool> NotifyReturnedForCorrectionAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(recordId))
				return false;

			var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
			if (record == null || record.State != (int)RmsRecordState.Returned || string.IsNullOrWhiteSpace(record.AuthorUserId))
				return false;

			cancellationToken.ThrowIfCancellationRequested();

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var departmentNumber = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var author = await _profiles.GetProfileByUserIdAsync(record.AuthorUserId, false);
			var reviewer = string.IsNullOrWhiteSpace(record.ReviewerUserId) ? null : await _profiles.GetProfileByUserIdAsync(record.ReviewerUserId, false);
			var reviewerName = reviewer == null ? null : $"{reviewer.FirstName} {reviewer.LastName}".Trim();

			var message = BuildReturnedForCorrectionMessage(record, reviewerName);

			try
			{
				return await _communication.SendNotificationAsync(record.AuthorUserId, departmentId, message, departmentNumber, department, ReturnedForCorrectionTitle, author);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Return-for-correction notification for record {recordId} could not be sent.");
				return false;
			}
		}

		public static string BuildReturnedForCorrectionMessage(RmsOperationalRecord record, string reviewerName)
		{
			if (record == null)
				throw new ArgumentNullException(nameof(record));

			var reference = string.IsNullOrWhiteSpace(record.RecordNumber) ? record.DraftReference : record.RecordNumber;
			var type = record.RecordType.HasValue ? ((RmsOperationalRecordType)record.RecordType.Value).ToString() : "Record";

			var builder = new StringBuilder();
			builder.Append(type).Append(" record ").Append(reference).Append(" was returned for correction");
			if (!string.IsNullOrWhiteSpace(reviewerName))
				builder.Append(" by ").Append(reviewerName);
			builder.Append('.');

			if (!string.IsNullOrWhiteSpace(record.ReturnReasonCode))
				builder.Append(" Reason: ").Append(record.ReturnReasonCode.Trim());

			if (!string.IsNullOrWhiteSpace(record.ReturnReasonText))
			{
				var note = record.ReturnReasonText.Trim();
				if (note.Length > MaxReasonTextLength)
					note = note.Substring(0, MaxReasonTextLength) + "…";
				builder.Append(" - ").Append(note);
			}

			var baseUrl = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/');
			builder.Append(' ').Append(baseUrl).Append("/User/Records/Details/").Append(record.RmsOperationalRecordId);

			return builder.ToString();
		}
	}
}
