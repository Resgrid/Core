using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The LMS report catalog (§13a.3, LMS add-on): every registered <see cref="ILmsReportSource"/>, saved reports in folders, CSV and PDF
	/// output, and scheduled delivery (daily, weekly or monthly at a department-local hour) by worker 94. Recipients are members who may
	/// read the department's training records (records managers and department admins) or the member a per-member report is about;
	/// inactive recipients are skipped at send time. The worker reads only the due definitions, asks the LMS gate once per department
	/// that has one, and claims each run before it sends (a concurrent worker cannot send it twice).
	/// </summary>
	public class LmsReportCatalogService : ILmsReportCatalogService
	{
		public const int DueBatch = 200;

		private readonly IReadOnlyList<ILmsReportSource> _sources;
		private readonly ILmsReportFoldersRepository _folders;
		private readonly ILmsReportDefinitionsRepository _definitions;
		private readonly ILmsReportRunsRepository _runs;
		private readonly IDepartmentsService _departments;
		private readonly IAuthorizationService _authorization;
		private readonly IUsersService _users;
		private readonly IUserProfileService _profiles;
		private readonly IEmailService _email;
		private readonly IPdfProvider _pdf;
		private readonly ILmsAccessService _access;
		private readonly IEventAggregator _events;

		public LmsReportCatalogService(IEnumerable<ILmsReportSource> sources, ILmsReportFoldersRepository folders, ILmsReportDefinitionsRepository definitions,
			ILmsReportRunsRepository runs, IDepartmentsService departments, IAuthorizationService authorization, IUsersService users, IUserProfileService profiles,
			IEmailService email, IPdfProvider pdf, ILmsAccessService access, IEventAggregator events)
		{
			_sources = (sources ?? Enumerable.Empty<ILmsReportSource>()).Where(s => s?.Descriptor?.Key != null)
				.GroupBy(s => s.Descriptor.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
			_folders = folders;
			_definitions = definitions;
			_runs = runs;
			_departments = departments;
			_authorization = authorization;
			_users = users;
			_profiles = profiles;
			_email = email;
			_pdf = pdf;
			_access = access;
			_events = events;
		}

		#region Catalog and output

		public IReadOnlyList<LmsReportDescriptor> GetCatalog() =>
			_sources.Select(s => s.Descriptor).OrderBy(d => d.Tier).ThenBy(d => d.Key, StringComparer.Ordinal).ToList();

		public LmsReportDescriptor Find(string reportKey) => Source(reportKey)?.Descriptor;

		private ILmsReportSource Source(string reportKey) =>
			string.IsNullOrWhiteSpace(reportKey) ? null : _sources.FirstOrDefault(s => string.Equals(s.Descriptor.Key, reportKey.Trim(), StringComparison.OrdinalIgnoreCase));

		public async Task<LmsReportTable> RunAsync(int departmentId, string userId, string reportKey, LmsReportParameters parameters, CultureInfo culture, DateTime utcNow)
		{
			var source = Source(reportKey) ?? throw new InvalidOperationException("lms_program_report_not_found");
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var localToday = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).TimeConverter(department).Date;
			parameters ??= new LmsReportParameters();
			var (fromLocal, toLocal) = LmsReportSchedule.ResolvePeriod(parameters, localToday);
			culture = LmsProgramText.CultureFor(culture?.Name);
			var request = new LmsReportRequest
			{
				DepartmentId = departmentId,
				UserId = userId,
				Parameters = parameters,
				FromUtc = LocalToUtc(fromLocal, department),
				ToUtc = LocalToUtc(toLocal, department),
				UtcNow = utcNow,
				Culture = culture,
				Department = department
			};

			var table = await source.BuildAsync(request, key => LmsProgramText.Get(key, culture)) ?? new LmsReportTable();
			if (source.Descriptor.UsesPeriod && string.IsNullOrEmpty(table.Subtitle))
				table.Subtitle = fromLocal.ToString("d", culture) + " - " + toLocal.AddDays(-1).ToString("d", culture);
			return table;
		}

		private static DateTime LocalToUtc(DateTime local, Department department) =>
			DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);

		public byte[] ToCsvBytes(LmsReportTable table)
		{
			var body = Encoding.UTF8.GetBytes(LmsReportSchedule.ToCsv(table));
			var preamble = Encoding.UTF8.GetPreamble();
			var bytes = new byte[preamble.Length + body.Length];
			Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
			Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
			return bytes;
		}

		/// <summary>The table as printable HTML: every value encoded, no script, no external resource.</summary>
		public static string ToHtml(LmsReportTable table, string departmentName, CultureInfo culture)
		{
			string H(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			culture ??= CultureInfo.GetCultureInfo("en");
			var html = new StringBuilder();
			html.Append("<!doctype html><html lang=\"").Append(H(culture.Name)).Append("\" dir=\"").Append(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">");
			html.Append("<title>").Append(H(table?.Title)).Append("</title><style>@page{size:landscape;margin:12mm}body{font-family:Arial,sans-serif;font-size:10px}")
				.Append("table{border-collapse:collapse;width:100%}td,th{border:1px solid #888;padding:4px;text-align:start;overflow-wrap:anywhere}thead{display:table-header-group}")
				.Append("tr{page-break-inside:avoid}h1{font-size:18px;margin:0 0 4px}p{margin:2px 0}</style></head><body>");
			html.Append("<h1>").Append(H(table?.Title)).Append("</h1>");
			if (!string.IsNullOrWhiteSpace(departmentName))
				html.Append("<p>").Append(H(departmentName)).Append("</p>");
			if (!string.IsNullOrWhiteSpace(table?.Subtitle))
				html.Append("<p>").Append(H(table.Subtitle)).Append("</p>");
			html.Append("<table><thead><tr>");
			foreach (var column in table?.Columns ?? new List<string>())
				html.Append("<th>").Append(H(column)).Append("</th>");
			html.Append("</tr></thead><tbody>");
			foreach (var row in table?.Rows ?? new List<List<string>>())
			{
				html.Append("<tr>");
				foreach (var cell in row)
					html.Append("<td>").Append(H(cell)).Append("</td>");
				html.Append("</tr>");
			}
			html.Append("</tbody></table>");
			foreach (var line in table?.Footer ?? new List<string>())
				html.Append("<p>").Append(H(line)).Append("</p>");
			html.Append("</body></html>");
			return html.ToString();
		}

		public byte[] ToPdfBytes(LmsReportTable table, string departmentName, CultureInfo culture)
		{
			var pageSize = string.Equals(culture?.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4";
			return _pdf.ConvertHtmlToPdf(ToHtml(table, departmentName, culture), pageSize);
		}

		#endregion Catalog and output

		#region Folders and saved reports

		public async Task<List<LmsReportFolder>> GetFoldersAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsReportFolder>() : await _folders.GetForDepartmentAsync(departmentId) ?? new List<LmsReportFolder>();

		public async Task<LmsReportFolder> SaveFolderAsync(LmsReportFolder folder, string userId, CancellationToken cancellationToken = default)
		{
			if (folder == null || folder.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_folder_not_found");
			var name = Trim(folder.Name, LmsReportFolder.NameMaxLength) ?? throw new InvalidOperationException("lms_program_name_required");
			LmsReportFolder stored;
			if (folder.LmsReportFolderId > 0)
				stored = await _folders.GetByIdForDepartmentAsync(folder.DepartmentId, folder.LmsReportFolderId) ?? throw new InvalidOperationException("lms_program_folder_not_found");
			else
				stored = new LmsReportFolder { DepartmentId = folder.DepartmentId, CreatedOn = DateTime.UtcNow, CreatedByUserId = userId };
			stored.Name = name;
			stored.SortOrder = folder.SortOrder;
			var saved = await _folders.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(folder.DepartmentId, userId, null, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteFolderAsync(int departmentId, int lmsReportFolderId, string userId, CancellationToken cancellationToken = default)
		{
			var folder = await _folders.GetByIdForDepartmentAsync(departmentId, lmsReportFolderId);
			if (folder == null)
				return false;
			// Its reports move out of the folder; nothing is deleted with it.
			await _definitions.ClearFolderAsync(departmentId, lmsReportFolderId, cancellationToken);
			var deleted = await _folders.DeleteAsync(folder, cancellationToken);
			Audit(departmentId, userId, JsonConvert.SerializeObject(folder), null);
			return deleted;
		}

		public async Task<List<LmsReportDefinition>> GetDefinitionsAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsReportDefinition>() : await _definitions.GetForDepartmentAsync(departmentId) ?? new List<LmsReportDefinition>();

		public async Task<LmsReportDefinition> GetDefinitionAsync(int departmentId, int lmsReportDefinitionId)
		{
			var definition = await _definitions.GetByIdForDepartmentAsync(departmentId, lmsReportDefinitionId);
			return definition == null || definition.IsDeleted || definition.DepartmentId != departmentId ? null : definition;
		}

		/// <summary>The saved parameters (defaults when unreadable).</summary>
		public static LmsReportParameters ParametersOf(LmsReportDefinition definition)
		{
			if (string.IsNullOrWhiteSpace(definition?.ParametersJson))
				return new LmsReportParameters();
			try
			{
				return JsonConvert.DeserializeObject<LmsReportParameters>(definition.ParametersJson) ?? new LmsReportParameters();
			}
			catch (JsonException)
			{
				return new LmsReportParameters();
			}
		}

		/// <summary>May receive the department's training reports: a records manager or a department admin.</summary>
		private async Task<bool> MayReceiveAsync(int departmentId, string userId)
		{
			var admins = await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>();
			if (admins.Any(a => string.Equals(a?.Id, userId, StringComparison.OrdinalIgnoreCase)))
				return true;
			var scope = await _authorization.GetTrainingManagementScopeAsync(userId, departmentId);
			return scope != null && scope.CanManageRecords;
		}

		public async Task<LmsReportDefinition> SaveDefinitionAsync(LmsReportDefinition definition, string userId, CancellationToken cancellationToken = default)
		{
			if (definition == null || definition.DepartmentId <= 0)
				throw new InvalidOperationException("lms_program_report_not_found");
			var source = Source(definition.ReportKey) ?? throw new InvalidOperationException("lms_program_report_not_found");
			var name = Trim(definition.Name, LmsReportDefinition.NameMaxLength) ?? throw new InvalidOperationException("lms_program_name_required");
			if (!Enum.IsDefined(typeof(LmsReportFormats), definition.Format) || !Enum.IsDefined(typeof(LmsReportFrequencies), definition.Frequency))
				throw new InvalidOperationException("lms_program_schedule_invalid");
			if (definition.HourLocal < 0 || definition.HourLocal > 23 || definition.DayOfWeek < 0 || definition.DayOfWeek > 6 || definition.DayOfMonth < 1 || definition.DayOfMonth > 28)
				throw new InvalidOperationException("lms_program_schedule_invalid");
			if (definition.LmsReportFolderId.HasValue && await _folders.GetByIdForDepartmentAsync(definition.DepartmentId, definition.LmsReportFolderId.Value) == null)
				throw new InvalidOperationException("lms_program_folder_not_found");

			var parameters = ParametersOf(definition);
			if (source.Descriptor.NeedsMember && string.IsNullOrWhiteSpace(parameters.UserId))
				throw new InvalidOperationException("lms_program_member_required");

			var recipients = definition.Recipients();
			if (recipients.Count > LmsReportDefinition.MaxRecipients)
				throw new InvalidOperationException("lms_program_too_many_recipients");
			if (definition.Frequency != (int)LmsReportFrequencies.None && recipients.Count == 0)
				throw new InvalidOperationException("lms_program_recipients_required");
			var active = await _departments.GetActiveMemberUserIdsAsync(definition.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var recipient in recipients)
				if (!active.Contains(recipient) || !(await MayReceiveAsync(definition.DepartmentId, recipient) || string.Equals(recipient, parameters.UserId, StringComparison.OrdinalIgnoreCase)))
					throw new InvalidOperationException("lms_program_recipient_invalid");

			var now = DateTime.UtcNow;
			LmsReportDefinition stored;
			string before = null;
			if (definition.LmsReportDefinitionId > 0)
			{
				stored = await GetDefinitionAsync(definition.DepartmentId, definition.LmsReportDefinitionId) ?? throw new InvalidOperationException("lms_program_report_not_found");
				before = JsonConvert.SerializeObject(stored);
				stored.UpdatedOn = now;
				stored.UpdatedByUserId = userId;
			}
			else
			{
				stored = new LmsReportDefinition { DepartmentId = definition.DepartmentId, CreatedOn = now, CreatedByUserId = userId };
			}

			stored.LmsReportFolderId = definition.LmsReportFolderId;
			stored.ReportKey = source.Descriptor.Key;
			stored.Name = name;
			stored.ParametersJson = JsonConvert.SerializeObject(parameters);
			stored.Format = definition.Format;
			stored.Frequency = definition.Frequency;
			stored.DayOfWeek = definition.DayOfWeek;
			stored.DayOfMonth = definition.DayOfMonth;
			stored.HourLocal = definition.HourLocal;
			stored.RecipientUserIds = string.Join(",", recipients);
			var department = await _departments.GetDepartmentByIdAsync(definition.DepartmentId, false);
			stored.NextRunUtc = LmsReportSchedule.NextRunUtc(stored, now, d => DateTime.SpecifyKind(d, DateTimeKind.Utc).TimeConverter(department), d => LocalToUtc(d, department));

			var saved = await _definitions.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(definition.DepartmentId, userId, before, JsonConvert.SerializeObject(saved));
			return saved;
		}

		public async Task<bool> DeleteDefinitionAsync(int departmentId, int lmsReportDefinitionId, string userId, CancellationToken cancellationToken = default)
		{
			var definition = await GetDefinitionAsync(departmentId, lmsReportDefinitionId);
			if (definition == null)
				return false;
			var before = JsonConvert.SerializeObject(definition);
			definition.IsDeleted = true;
			definition.NextRunUtc = null;
			definition.UpdatedOn = DateTime.UtcNow;
			definition.UpdatedByUserId = userId;
			await _definitions.SaveOrUpdateAsync(definition, cancellationToken, true);
			Audit(departmentId, userId, before, JsonConvert.SerializeObject(definition));
			return true;
		}

		public async Task<List<LmsReportRun>> GetRunsAsync(int departmentId, int lmsReportDefinitionId) =>
			await _runs.GetForDefinitionAsync(departmentId, lmsReportDefinitionId, 20) ?? new List<LmsReportRun>();

		#endregion Folders and saved reports

		#region Scheduled delivery (worker 94)

		public async Task<int> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var handled = 0;
			var due = await _definitions.GetDueAsync(utcNow, DueBatch) ?? new List<LmsReportDefinition>();
			foreach (var department in due.GroupBy(d => d.DepartmentId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				bool entitled;
				try
				{
					// The gate once per department that has a due report (R-L9), never per report.
					entitled = await _access.CanAuthorAndManageAsync(department.Key);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"LMS gate for scheduled reports of department {department.Key} could not be read.");
					continue;
				}

				foreach (var definition in department)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (await RunOneAsync(definition, entitled, utcNow, cancellationToken))
							handled++;
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Scheduled LMS report {definition.LmsReportDefinitionId} failed.");
					}
				}
			}

			return handled;
		}

		/// <summary>Claims and sends one due report; false when another worker had claimed it.</summary>
		public async Task<bool> RunOneAsync(LmsReportDefinition definition, bool entitled, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (definition?.NextRunUtc == null)
				return false;
			var department = await _departments.GetDepartmentByIdAsync(definition.DepartmentId, false);
			var next = LmsReportSchedule.NextRunUtc(definition, utcNow, d => DateTime.SpecifyKind(d, DateTimeKind.Utc).TimeConverter(department), d => LocalToUtc(d, department));
			if (!await _definitions.TryClaimRunAsync(definition.LmsReportDefinitionId, definition.NextRunUtc.Value, next, utcNow, cancellationToken))
				return false;

			var run = new LmsReportRun { DepartmentId = definition.DepartmentId, LmsReportDefinitionId = definition.LmsReportDefinitionId, RanOn = utcNow };
			try
			{
				var active = await _departments.GetActiveMemberUserIdsAsync(definition.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var parameters = ParametersOf(definition);
				var recipients = new List<string>();
				foreach (var recipient in definition.Recipients().Where(active.Contains))
					if (await MayReceiveAsync(definition.DepartmentId, recipient) || string.Equals(recipient, parameters.UserId, StringComparison.OrdinalIgnoreCase))
						recipients.Add(recipient);

				if (!entitled || department == null || Source(definition.ReportKey) == null || !active.Contains(definition.CreatedByUserId ?? string.Empty) || recipients.Count == 0)
				{
					run.Status = (int)LmsReportRunStatuses.Skipped;
					run.Error = !entitled ? "lms_inactive" : recipients.Count == 0 ? "no_active_recipient" : "owner_or_report_unavailable";
					return true;
				}

				// One build per language; each recipient gets the report in their own.
				var built = new Dictionary<string, (byte[] Bytes, LmsReportTable Table)>(StringComparer.OrdinalIgnoreCase);
				var sent = 0;
				foreach (var recipient in recipients)
				{
					var user = _users.GetUserById(recipient, false);
					if (string.IsNullOrWhiteSpace(user?.Email))
						continue;
					var culture = LmsProgramText.CultureFor((await _profiles.GetProfileByUserIdAsync(recipient))?.Language);
					if (!built.TryGetValue(culture.Name, out var output))
					{
						var table = await RunAsync(definition.DepartmentId, definition.CreatedByUserId, definition.ReportKey, parameters, culture, utcNow);
						var bytes = definition.Format == (int)LmsReportFormats.Pdf ? ToPdfBytes(table, department.Name, culture) : ToCsvBytes(table);
						built[culture.Name] = output = (bytes, table);
					}

					run.RowTotal = output.Table.Rows.Count;
					var extension = definition.Format == (int)LmsReportFormats.Pdf ? ".pdf" : ".csv";
					var email = new EmailNotification
					{
						To = user.Email,
						Subject = definition.Name,
						AttachmentName = FileName(definition.Name) + "-" + utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + extension,
						AttachmentData = output.Bytes
					};
					var link = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/LmsPrograms/Reports";
					if (await _email.SendReportDeliveryAsync(email, definition.DepartmentId, link, definition.Name))
						sent++;
				}

				run.Recipients = sent;
				run.Status = sent > 0 ? (int)LmsReportRunStatuses.Sent : (int)LmsReportRunStatuses.Failed;
				if (sent == 0)
					run.Error = "not_sent";
				return true;
			}
			catch (Exception ex)
			{
				run.Status = (int)LmsReportRunStatuses.Failed;
				run.Error = Trim(ex.Message, LmsReportRun.ErrorMaxLength);
				throw;
			}
			finally
			{
				try
				{
					await _runs.SaveOrUpdateAsync(run, CancellationToken.None, true);
					await _definitions.SetLastRunStatusAsync(definition.LmsReportDefinitionId, run.Status, CancellationToken.None);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Run history of scheduled LMS report {definition.LmsReportDefinitionId} was not stored.");
				}
			}
		}

		private static string FileName(string name)
		{
			var text = new string((name ?? "report").Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
			while (text.Contains("--"))
				text = text.Replace("--", "-");
			return text.Length == 0 ? "report" : text.Length > 60 ? text.Substring(0, 60) : text;
		}

		#endregion Scheduled delivery (worker 94)

		private static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, string before, string after)
		{
			_events?.SendMessage(new AuditEvent { DepartmentId = departmentId, UserId = userId, Type = AuditLogTypes.LmsReportDefinitionChanged, Before = before, After = after, Successful = true });
		}
	}
}
