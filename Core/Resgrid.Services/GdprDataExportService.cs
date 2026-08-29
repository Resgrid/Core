using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Localization.Areas.User.SystemMessages;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class GdprDataExportService : IGdprDataExportService
	{
		private readonly IGdprDataExportRequestRepository _repository;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentMemberSensitiveDataService _memberSensitiveDataService;
		private readonly IDepartmentMemberEmergencyContactService _emergencyContactService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IMessageService _messageService;
		private readonly ICertificationService _certificationService;
		private readonly ITrainingService _trainingService;
		private readonly IShiftsService _shiftsService;
		private readonly IEmailService _emailService;

		public GdprDataExportService(
			IGdprDataExportRequestRepository repository,
			IUserProfileService userProfileService,
			IDepartmentMemberSensitiveDataService memberSensitiveDataService,
			IDepartmentMemberEmergencyContactService emergencyContactService,
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IActionLogsService actionLogsService,
			IMessageService messageService,
			ICertificationService certificationService,
			ITrainingService trainingService,
			IShiftsService shiftsService,
			IEmailService emailService)
		{
			_repository = repository;
			_userProfileService = userProfileService;
			_memberSensitiveDataService = memberSensitiveDataService;
			_emergencyContactService = emergencyContactService;
			_usersService = usersService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_actionLogsService = actionLogsService;
			_messageService = messageService;
			_certificationService = certificationService;
			_trainingService = trainingService;
			_shiftsService = shiftsService;
			_emailService = emailService;
		}

		public async Task<GdprDataExportRequest> CreateExportRequestAsync(string userId, int departmentId, CancellationToken cancellationToken = default)
		{
			var request = new GdprDataExportRequest
			{
				UserId = userId,
				DepartmentId = departmentId,
				Status = (int)GdprExportStatus.Pending,
				RequestedOn = DateTime.UtcNow
			};

			return await _repository.SaveOrUpdateAsync(request, cancellationToken, true);
		}

		public async Task<GdprDataExportRequest> GetActiveRequestByUserIdAsync(string userId)
		{
			return await _repository.GetActiveRequestByUserIdAsync(userId);
		}

		public async Task<GdprDataExportRequest> GetRequestByTokenAsync(string token)
		{
			return await _repository.GetByTokenAsync(token);
		}

		public async Task ProcessPendingRequestsAsync(CancellationToken cancellationToken = default)
		{
			var pending = await _repository.GetPendingRequestsAsync();
			if (pending == null) return;

			foreach (var request in pending)
			{
				var claimed = await _repository.TryClaimForProcessingAsync(request.GdprDataExportRequestId, cancellationToken);
				if (!claimed)
					continue;

				try
				{
					var zipBytes = await BuildExportZipAsync(request.UserId, request.DepartmentId);

					var tokenBytes = new byte[32];
					RandomNumberGenerator.Fill(tokenBytes);
					var token = Convert.ToBase64String(tokenBytes)
						.Replace('+', '-').Replace('/', '_').TrimEnd('=');

					request.Status = (int)GdprExportStatus.Completed;
					request.ProcessingStartedOn = DateTime.UtcNow;
					request.CompletedOn = DateTime.UtcNow;
					request.ExportData = zipBytes;
					request.FileSizeBytes = zipBytes.LongLength;
					request.DownloadToken = token;
					request.TokenExpiresAt = DateTime.UtcNow.AddDays(7);
					await _repository.SaveOrUpdateAsync(request, cancellationToken, true);

					var profile = await _userProfileService.GetProfileByUserIdAsync(request.UserId);
					var user = _usersService.GetUserById(request.UserId);
					if (profile != null && user != null)
					{
						var downloadUrl = $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Home/DownloadMyData?token={token}";
						await _emailService.SendGdprDataExportReadyAsync(
							user.Email,
							profile.FirstName,
							downloadUrl,
							request.TokenExpiresAt.Value,
							profile.Language);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					request.Status = (int)GdprExportStatus.Failed;
					request.ErrorMessage = ex.Message;
					await _repository.SaveOrUpdateAsync(request, cancellationToken, true);
				}
			}
		}

		public async Task ExpireOldRequestsAsync(CancellationToken cancellationToken = default)
		{
			var expired = await _repository.GetExpiredRequestsAsync();
			if (expired == null) return;

			foreach (var request in expired)
			{
				request.Status = (int)GdprExportStatus.Expired;
				request.ExportData = null;
				await _repository.SaveOrUpdateAsync(request, cancellationToken, true);
			}
		}

		public async Task MarkDownloadedAsync(GdprDataExportRequest request, CancellationToken cancellationToken = default)
		{
			request.DownloadToken = null;
			request.TokenExpiresAt = DateTime.UtcNow;
			request.Status = (int)GdprExportStatus.Expired;
			request.ExportData = null;
			await _repository.SaveOrUpdateAsync(request, cancellationToken, true);
		}

		private async Task<byte[]> BuildExportZipAsync(string userId, int departmentId)
		{
			var ledger = new RedactionLedger();

			using var ms = new MemoryStream();
			using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
			{
				await AddJsonEntry(archive, "profile.json", await BuildProfileDataAsync(userId), ledger);
				await AddJsonEntry(archive, "membership.json", await BuildMembershipDataAsync(userId, departmentId), ledger);
				await AddJsonEntry(archive, "action_logs.json", await BuildActionLogsDataAsync(userId), ledger);
				await AddJsonEntry(archive, "messages_inbox.json", await BuildInboxMessagesDataAsync(userId), ledger);
				await AddJsonEntry(archive, "messages_sent.json", await BuildSentMessagesDataAsync(userId), ledger);
				await AddJsonEntry(archive, "certifications.json", await BuildCertificationsDataAsync(userId), ledger);
				await AddJsonEntry(archive, "trainings.json", await BuildTrainingsDataAsync(userId), ledger);
				await AddJsonEntry(archive, "shifts.json", await BuildShiftsDataAsync(userId), ledger);

				// Written last, so it can report what every other entry withheld. Only present when
				// something actually was: a member of an unprotected department gets the archive they
				// always got, with no extra file to explain.
				if (ledger.Total > 0)
				{
					var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
					await AddManifestEntry(archive, ledger, profile?.Language);
				}
			}

			return ms.ToArray();
		}

		/// <summary>
		/// Counts of protected values held back from the archive, by entry and field path.
		/// </summary>
		private sealed class RedactionLedger
		{
			public readonly Dictionary<string, SortedSet<string>> Fields =
				new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

			public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);

			public int Total { get; private set; }

			public void Record(string fileName, string path)
			{
				if (!Fields.TryGetValue(fileName, out var paths))
				{
					paths = new SortedSet<string>(StringComparer.Ordinal);
					Fields[fileName] = paths;
					Counts[fileName] = 0;
				}

				paths.Add(path);
				Counts[fileName] = Counts[fileName] + 1;
				Total++;
			}
		}

		private static async Task AddJsonEntry(ZipArchive archive, string fileName, object data, RedactionLedger ledger)
		{
			var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
			using var entryStream = entry.Open();

			var json = JsonConvert.SerializeObject(data, Formatting.Indented);
			json = Sanitize(json, fileName, ledger);

			var bytes = Encoding.UTF8.GetBytes(json);
			await entryStream.WriteAsync(bytes, 0, bytes.Length);
		}

		/// <summary>
		/// Last line of defence before anything is written to the archive: walks the serialized JSON
		/// and replaces every ADP envelope with the REDACTED placeholder.
		///
		/// Deliberately shape-driven rather than a per-entity field list. This export runs unattended
		/// with no protected-data grant (plan section 3.4 — background jobs cannot obtain user
		/// grants), so a cataloged value reaches it as ciphertext, and the archive is stored in the
		/// database for up to seven days. A field list would have to be revisited every time the
		/// catalog grows or an entry is added here, and the failure mode of forgetting is silent
		/// ciphertext in a member's download. Detecting the envelope itself cannot be forgotten.
		/// </summary>
		private static string Sanitize(string json, string fileName, RedactionLedger ledger)
		{
			if (string.IsNullOrWhiteSpace(json))
				return json;

			JToken root;
			try
			{
				root = JToken.Parse(json);
			}
			catch (JsonException ex)
			{
				// Unparseable output should never happen — it was just serialized. Fail closed rather
				// than shipping bytes nothing has inspected.
				Logging.LogException(ex, $"GDPR export: could not inspect {fileName} for protected values");
				throw;
			}

			// Descendants() lives on JContainer; an entry that serialized to a bare scalar still has
			// to be inspected, so handle that case rather than skipping it.
			var values = root is JContainer container
				? container.Descendants().OfType<JValue>().ToList()
				: root is JValue rootValue
					? new List<JValue> { rootValue }
					: new List<JValue>();

			foreach (var value in values)
			{
				if (value.Type != JTokenType.String)
					continue;

				var text = value.Value<string>();
				var path = NormalizePath(value.Path);

				// Already redacted upstream (membership data is resolved through the read pipeline).
				// Not rewritten, but still reported — otherwise the manifest would claim nothing was
				// withheld from an entry the member can see gaps in.
				if (string.Equals(text, ProtectedDataEnvelope.RedactionValue, StringComparison.Ordinal))
				{
					ledger.Record(fileName, path);
					continue;
				}

				if (!IsProtectedPayload(text))
					continue;

				value.Value = ProtectedDataEnvelope.RedactionValue;
				ledger.Record(fileName, path);
			}

			return root.ToString(Formatting.Indented);
		}

		/// <summary>
		/// True for a text envelope, and for a binary envelope that has been serialized as base64.
		/// A byte[] carrying <c>rgdpb:</c> reaches JSON as base64, so its prefix is checked in that
		/// encoding rather than decoding what may be a very large payload.
		/// </summary>
		private static bool IsProtectedPayload(string value)
		{
			if (ProtectedDataEnvelope.HasEnvelopePrefix(value))
				return true;

			return value != null && value.Length >= BinaryPrefixBase64.Length &&
				   value.StartsWith(BinaryPrefixBase64, StringComparison.Ordinal);
		}

		/// <summary>
		/// Base64 of the binary envelope prefix. The prefix is six bytes, which encodes to exactly
		/// eight base64 characters with no padding, so any base64 payload that starts with those
		/// bytes starts with this string.
		/// </summary>
		private static readonly string BinaryPrefixBase64 =
			Convert.ToBase64String(Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix));

		/// <summary>
		/// Collapses array indices so the manifest names a field once rather than once per row —
		/// "certifications[0].name" and "certifications[41].name" are the same withheld field.
		/// </summary>
		private static string NormalizePath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return path;

			var builder = new StringBuilder(path.Length);
			var inIndex = false;

			foreach (var c in path)
			{
				if (c == '[')
				{
					inIndex = true;
					builder.Append("[]");
					continue;
				}

				if (c == ']')
				{
					inIndex = false;
					continue;
				}

				if (!inIndex)
					builder.Append(c);
			}

			return builder.ToString();
		}

		/// <summary>
		/// Says plainly what the archive does not contain and how to get it. An export that silently
		/// drops a member's own data is worse than one that admits to it — this is a subject access
		/// request, and the gap is the part they will ask about.
		/// </summary>
		private static async Task AddManifestEntry(ZipArchive archive, RedactionLedger ledger, string culture)
		{
			var entries = ledger.Fields.ToDictionary(
				kvp => kvp.Key,
				kvp => (object)new { valuesWithheld = ledger.Counts[kvp.Key], fields = kvp.Value.ToArray() },
				StringComparer.Ordinal);

			var manifest = new
			{
				notice = SystemMessagesResources.Get("GdprExportWithheldNotice", culture),
				howToObtain = SystemMessagesResources.Get("GdprExportWithheldHowTo", culture),
				placeholder = ProtectedDataEnvelope.RedactionValue,
				totalValuesWithheld = ledger.Total,
				entries
			};

			var entry = archive.CreateEntry("withheld.json", CompressionLevel.Optimal);
			using var entryStream = entry.Open();
			var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest, Formatting.Indented));
			await entryStream.WriteAsync(bytes, 0, bytes.Length);
		}

		private async Task<object> BuildProfileDataAsync(string userId)
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			var user = _usersService.GetUserById(userId);
			return new { profile, user = user != null ? new { user.Id, user.Email, user.UserName } : null };
		}

		private async Task<object> BuildMembershipDataAsync(string userId, int departmentId)
		{
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

			// The identification number, addresses and emergency contacts are department-scoped
			// (ADP plan 5.1) and no longer reachable through profile.json, so a subject access
			// request would come back short without them. Resolved for read: this export runs
			// unattended with no grant, so a protected department yields the REDACTED placeholder
			// rather than raw ciphertext.
			var sensitiveByUser = await _memberSensitiveDataService.GetResolvedForDepartmentAsync(
				departmentId, null, userId);
			sensitiveByUser.TryGetValue(userId, out var sensitive);
			var emergencyContacts = await _emergencyContactService.GetAllForMemberAsync(departmentId, userId);

			return new { member, group, roles, sensitive, emergencyContacts };
		}

		private async Task<object> BuildActionLogsDataAsync(string userId)
		{
			var logs = await _actionLogsService.GetAllActionLogsForUser(userId);
			return logs;
		}

		private async Task<object> BuildInboxMessagesDataAsync(string userId)
		{
			var messages = await _messageService.GetInboxMessagesByUserIdAsync(userId);
			return messages;
		}

		private async Task<object> BuildSentMessagesDataAsync(string userId)
		{
			var messages = await _messageService.GetSentMessagesByUserIdAsync(userId);
			return messages;
		}

		private async Task<object> BuildCertificationsDataAsync(string userId)
		{
			var certs = await _certificationService.GetCertificationsByUserIdAsync(userId);
			if (certs != null)
			{
				return certs.Select(c => new
				{
					c.PersonnelCertificationId,
					c.UserId,
					c.DepartmentId,
					c.Name,
					c.Number,
					c.ExpiresOn,
					c.RecievedOn,
					c.Type
				});
			}
			return new List<object>();
		}

		private async Task<object> BuildTrainingsDataAsync(string userId)
		{
			var trainings = await _trainingService.GetTrainingUsersForUserAsync(userId);
			return trainings;
		}

		private async Task<object> BuildShiftsDataAsync(string userId)
		{
			var shifts = await _shiftsService.GetShiftPersonsForUserAsync(userId);
			return shifts;
		}
	}
}
