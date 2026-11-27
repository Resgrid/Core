using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// LmsExports text read culture-explicitly (round 9C): the training hours / CE / CPD file's headings are written in the language of the
	/// person building it, whatever thread builds the file.
	/// </summary>
	public static class LmsExportText
	{
		private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.LmsExports.LmsExports", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		public static string Get(string key, CultureInfo culture)
		{
			if (string.IsNullOrEmpty(key))
				return string.Empty;
			var language = CultureInfo.GetCultureInfo((culture ?? CultureInfo.GetCultureInfo("en")).TwoLetterISOLanguageName);
			try
			{
				return Strings.GetResourceSet(language, true, false)?.GetString(key)
					?? Strings.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)?.GetString(key)
					?? key;
			}
			catch (MissingManifestResourceException)
			{
				return key;
			}
		}
	}

	/// <summary>
	/// CE reporting files (Training T5, §13a "CE and state reporting"): a training hours / CE / CPD file for any reporting body or employer in any
	/// country (round 9C: ISO dates, headings in the builder's language), the CAPCE completion file for a department that is a CAPCE-accredited
	/// provider, state CE files (Florida's CE Broker layout; the configurable CSV fallback for Virginia, New Jersey, Florida fire and any
	/// other state) and the state-ready roster. Builds files only — nothing is submitted to CAPCE or a state. Records stay exportable after a
	/// lapse (§17.3 rule 2), so building needs the add-on active or lapsed; changing the settings needs it active (rule 3). Only active
	/// members' completions go in unless former members are asked for (the explicit switch, §12.2).
	/// </summary>
	public class LmsCeExportService : ILmsCeExportService
	{
		private const int Page = 1000;

		private readonly IDepartmentSettingsService _settings;
		private readonly ILearningRecordService _learningRecords;
		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IEventAggregator _events;

		private readonly IDepartmentMemberSensitiveDataService _sensitiveData;
		private readonly IProtectedReadService _protectedRead;
		private readonly IProtectedGrantContext _grant;

		public LmsCeExportService(IDepartmentSettingsService settings, ILearningRecordService learningRecords, ICertificationService certifications, IDepartmentsService departments,
			IUserProfileService profiles, IEventAggregator events, IDepartmentMemberSensitiveDataService sensitiveData, IProtectedReadService protectedRead, IProtectedGrantContext grant)
		{
			_sensitiveData = sensitiveData;
			_protectedRead = protectedRead;
			_grant = grant;
			_settings = settings;
			_learningRecords = learningRecords;
			_certifications = certifications;
			_departments = departments;
			_profiles = profiles;
			_events = events;
		}

		public IReadOnlyList<LmsCeExporter> GetExporters() => LmsCeExporterCatalog.All;

		/// <summary>The certification numbers through the protected read pipeline; on a fault every sealed number is withheld, never written as ciphertext.</summary>
		private async Task ResolveNumbersAsync(int departmentId, List<PersonnelCertification> certifications, string grantToken, string userId, CancellationToken cancellationToken)
		{
			if (certifications.Count == 0)
				return;
			try
			{
				if (_protectedRead != null)
					await _protectedRead.ResolveCertificationsForReadAsync(departmentId, certifications, grantToken, userId, false, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"Certification numbers could not be resolved for a CE export of department {departmentId}; they are withheld.");
			}

			foreach (var certification in certifications)
				if (ProtectedDataEnvelope.HasEnvelopePrefix(certification.Number))
					certification.Number = ProtectedDataEnvelope.RedactionValue;
		}

		public bool CanBuild(LmsActor actor, bool isRecordsManager) =>
			actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || isRecordsManager) && actor.Gate != null && actor.Gate.FlagOn &&
			(actor.Gate.CanAuthor || actor.Gate.IsLapsed);

		public bool CanConfigure(LmsActor actor) => actor != null && actor.IsCurrentMember && actor.IsDepartmentAdmin && actor.Gate != null && actor.Gate.CanAuthor;

		public async Task<LmsCeReportingSettings> GetSettingsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new LmsCeReportingSettings().Normalized();

			var setting = await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.LmsCeReportingSettings);
			if (!string.IsNullOrWhiteSpace(setting?.Setting))
			{
				try
				{
					var stored = ObjectSerialization.Deserialize<LmsCeReportingSettings>(setting.Setting);
					if (stored != null)
						return stored.Normalized();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Unreadable CE reporting settings for department {departmentId}");
				}
			}

			return new LmsCeReportingSettings().Normalized();
		}

		private async Task<LmsCeReportingSettings> StoreAsync(LmsActor actor, LmsCeReportingSettings before, LmsCeReportingSettings after, CancellationToken cancellationToken)
		{
			await _settings.SaveOrUpdateSettingAsync(actor.DepartmentId, ObjectSerialization.Serialize(after), DepartmentSettingTypes.LmsCeReportingSettings, cancellationToken);
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsCeReportingSettingsChanged,
				Before = JsonConvert.SerializeObject(before),
				After = JsonConvert.SerializeObject(after),
				Successful = true
			});
			return after;
		}

		public async Task<LmsCeReportingSettings> SaveSettingsAsync(LmsActor actor, LmsCeReportingSettings settings, CancellationToken cancellationToken = default)
		{
			if (!CanConfigure(actor))
				throw new InvalidOperationException("lms_ce_not_authorized");

			var before = await GetSettingsAsync(actor.DepartmentId);
			var after = (settings ?? new LmsCeReportingSettings()).Normalized();
			// The settings form does not carry the column maps; they are kept as stored.
			after.ColumnMaps = before.ColumnMaps;

			var typeIds = new[] { after.LicenseCertificationTypeId, after.NremtCertificationTypeId, after.NemsidCertificationTypeId }.Where(id => id > 0).ToList();
			if (typeIds.Count > 0)
			{
				var known = (await _certifications.GetAllCertificationTypesByDepartmentAsync(actor.DepartmentId) ?? new List<DepartmentCertificationType>())
					.Where(t => t != null && t.DepartmentId == actor.DepartmentId).Select(t => t.DepartmentCertificationTypeId).ToHashSet();
				if (typeIds.Any(id => !known.Contains(id)))
					throw new InvalidOperationException("lms_ce_certification_type_invalid");
			}

			return await StoreAsync(actor, before, after, cancellationToken);
		}

		public async Task<LmsCeReportingSettings> SaveColumnsAsync(LmsActor actor, string exporterKey, IEnumerable<LmsCeColumn> columns, CancellationToken cancellationToken = default)
		{
			if (!CanConfigure(actor))
				throw new InvalidOperationException("lms_ce_not_authorized");
			var exporter = LmsCeExporterCatalog.Find(exporterKey) ?? throw new InvalidOperationException("lms_ce_exporter_unknown");

			var before = await GetSettingsAsync(actor.DepartmentId);
			var after = before.Normalized();
			after.ColumnMaps.RemoveAll(m => string.Equals(m.ExporterKey, exporter.Key, StringComparison.OrdinalIgnoreCase));
			var normalized = LmsCeExportRules.NormalizeColumns(columns);
			if (normalized.Count > 0)
				after.ColumnMaps.Add(new LmsCeColumnMap { ExporterKey = exporter.Key, Columns = normalized });

			return await StoreAsync(actor, before, after.Normalized(), cancellationToken);
		}

		public async Task<LmsCeExportFile> BuildAsync(LmsActor actor, bool isRecordsManager, LmsCeExportRequest request, bool audit, CancellationToken cancellationToken = default)
		{
			var exporter = LmsCeExporterCatalog.Find(request?.ExporterKey);
			if (exporter == null)
				return new LmsCeExportFile { Problem = "Ce.Problem.UnknownExporter" };
			if (!CanBuild(actor, isRecordsManager))
				return new LmsCeExportFile { Exporter = exporter, Problem = "Ce.Problem.NotAuthorized" };

			var departmentId = actor.DepartmentId;
			var settings = await GetSettingsAsync(departmentId);
			var columns = settings.ColumnsFor(exporter);
			if (exporter.LocalizedHeaders)
				LmsCeExportRules.LocalizeHeaders(columns, key => LmsExportText.Get(key, request.Culture));
			if (exporter.Kind == LmsCeExporterKinds.Capce && string.IsNullOrWhiteSpace(settings.CapceProviderNumber))
				return new LmsCeExportFile { Exporter = exporter, Problem = "Ce.Problem.NoCapceProvider" };
			if (columns.Any(c => c.Source == (int)LmsCeColumnSources.CeBrokerProviderNumber) && string.IsNullOrWhiteSpace(settings.CeBrokerProviderNumber))
				return new LmsCeExportFile { Exporter = exporter, Problem = "Ce.Problem.NoCeBrokerProvider" };

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var fromLocal = request.FromLocal.Date;
			var toLocal = request.ToLocal.Date;
			if (toLocal < fromLocal)
				(fromLocal, toLocal) = (toLocal, fromLocal);
			var fromUtc = DateTime.SpecifyKind(DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);
			var toUtc = DateTime.SpecifyKind(DateTime.SpecifyKind(toLocal.AddDays(1), DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);

			var records = new List<LearningRecord>();
			for (var skip = 0; ; skip += Page)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var page = await _learningRecords.QueryAsync(new LearningRecordQuery
				{
					DepartmentId = departmentId,
					From = fromUtc,
					To = toUtc,
					Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
					CategoryIds = request.CategoryId.HasValue ? new List<int> { request.CategoryId.Value } : null,
					Skip = skip,
					Take = Page
				}) ?? new List<LearningRecord>();
				records.AddRange(page);
				if (page.Count < Page)
					break;
			}

			// ADP: identification and licence numbers are protected personnel data. They are revealed only for a caller holding a grant (the
			// reveal module's download), and are REDACTED otherwise, exactly as the reports and the certification pages withhold them.
			var grantToken = _grant?.GrantToken;
			var memberList = await LmsExportMembers.LoadAsync(_departments, _profiles, _sensitiveData, departmentId, records.Select(r => r.UserId), grantToken, actor.UserId);
			var members = memberList.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var selected = LmsCeExportRules.Select(exporter, records, settings, fromUtc, toUtc, members, request.IncludeFormerMembers, request.CategoryId, departmentId);

			var userIds = selected.Select(r => r.UserId).Where(id => id != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var identities = new Dictionary<string, LmsCeMemberIdentity>(StringComparer.OrdinalIgnoreCase);
			if (userIds.Count > 0 && (settings.LicenseCertificationTypeId > 0 || settings.NremtCertificationTypeId > 0 || settings.NemsidCertificationTypeId > 0))
			{
				var certifications = await _certifications.GetCertificationsForDepartmentAsync(departmentId, userIds) ?? new List<PersonnelCertification>();
				await ResolveNumbersAsync(departmentId, certifications, grantToken, actor.UserId, cancellationToken);
				foreach (var group in certifications.Where(c => c?.UserId != null && c.DepartmentId == departmentId).GroupBy(c => c.UserId, StringComparer.OrdinalIgnoreCase))
					identities[group.Key] = LmsCeExportRules.Identity(group, settings);
			}

			var categories = (await _learningRecords.GetCategoriesAsync(departmentId, true) ?? new List<TrainingCategory>())
				.GroupBy(c => c.TrainingCategoryId).ToDictionary(g => g.Key, g => g.First());
			var file = LmsCeExportRules.Build(exporter, columns, selected, members, identities, settings, categories,
				utc => department == null ? utc : utc.TimeConverter(department));
			file.FileName = exporter.FilePrefix + "-" + fromLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + toLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv";
			file.Bytes = LmsCeExportRules.ToBytes(file);

			if (audit)
				_events?.SendMessage(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = actor.UserId,
					Type = AuditLogTypes.LmsCeExportDownloaded,
					After = JsonConvert.SerializeObject(new
					{
						Exporter = exporter.Key, From = fromLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), To = toLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
						request.IncludeFormerMembers, request.CategoryId, Rows = file.Rows.Count
					}),
					Successful = true
				});

			return file;
		}
	}
}
