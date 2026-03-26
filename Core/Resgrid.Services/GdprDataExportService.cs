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
							request.TokenExpiresAt.Value);
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
			using var ms = new MemoryStream();
			using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
			{
				await AddJsonEntry(archive, "profile.json", await BuildProfileDataAsync(userId));
				await AddJsonEntry(archive, "membership.json", await BuildMembershipDataAsync(userId, departmentId));
				await AddJsonEntry(archive, "action_logs.json", await BuildActionLogsDataAsync(userId));
				await AddJsonEntry(archive, "messages_inbox.json", await BuildInboxMessagesDataAsync(userId));
				await AddJsonEntry(archive, "messages_sent.json", await BuildSentMessagesDataAsync(userId));
				await AddJsonEntry(archive, "certifications.json", await BuildCertificationsDataAsync(userId));
				await AddJsonEntry(archive, "trainings.json", await BuildTrainingsDataAsync(userId));
				await AddJsonEntry(archive, "shifts.json", await BuildShiftsDataAsync(userId));
			}

			return ms.ToArray();
		}

		private static async Task AddJsonEntry(ZipArchive archive, string fileName, object data)
		{
			var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
			using var entryStream = entry.Open();
			var json = JsonConvert.SerializeObject(data, Formatting.Indented);
			var bytes = Encoding.UTF8.GetBytes(json);
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
			return new { member, group, roles };
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
