using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Chat;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Chat moderation: flags, moderator actions (delete/mute/ban/lock), department chat settings and
	/// records-request exports
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize(Policy = ResgridResources.Chat_View)]
	public class ChatModerationController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors

		private readonly IChatModerationService _chatModerationService;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IChatMessageService _chatMessageService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;

		// Rule 87: bulk transcript exports carry PII and require MFA re-authentication within a short window.
		// The bearer API is stateless (no session), so a fresh step-up proof is held server-side in the cache,
		// written by VerifyExportMfa after a valid TOTP and read by RequestExport.
		private const int ExportMfaWindowMinutes = 5;

		// TOTP is only a 6-digit code, so verification attempts are throttled per user to defeat brute force.
		// A code rotates every ~30s, so a legitimate caller needs very few tries per window. Fixed (not
		// config-gated) so the brute-force guard can never be accidentally disabled by a zero/misconfig.
		private const int MfaVerifyMaxAttemptsPerWindow = 5;
		private static readonly TimeSpan MfaVerifyRateLimitWindow = TimeSpan.FromMinutes(1);
		private static string GetMfaVerifyRateLimitCacheKey(string userId) => $"chat:rl:mfa:{userId}";

		public ChatModerationController(
			IChatModerationService chatModerationService,
			IChatChannelService chatChannelService,
			IChatPermissionService chatPermissionService,
			IChatMessageService chatMessageService,
			IFeatureToggleService featureToggleService,
			IAuthorizationService authorizationService,
			IEventAggregator eventAggregator,
			ICacheProvider cacheProvider,
			UserManager<Model.Identity.IdentityUser> userManager)
		{
			_chatModerationService = chatModerationService;
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_chatMessageService = chatMessageService;
			_featureToggleService = featureToggleService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
			_userManager = userManager;
		}

		private static string GetExportMfaProofCacheKey(string userId) => $"chat:export:mfa:{userId}";

		#endregion Members and Constructors

		#region Flags

		/// <summary>
		/// Returns flagged messages for the department filtered by status. Department admins only.
		/// </summary>
		/// <param name="status">Flag status filter (0 = Open, 1 = Reviewed, 2 = Dismissed, 3 = ActionTaken)</param>
		/// <param name="page">Page number</param>
		/// <param name="pageSize">Page size</param>
		/// <returns>Array of ChatFlagResultData objects</returns>
		[HttpGet("GetFlags")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatFlagsResult>> GetFlags(int status = 0, int page = 0, int pageSize = 50)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var result = new GetChatFlagsResult();
			var flags = await _chatModerationService.GetFlagsAsync(DepartmentId, UserId, (ChatFlagStatus)status, page, pageSize);

			if (flags != null && flags.Any())
			{
				foreach (var flag in flags)
				{
					result.Data.Add(ConvertFlagResultData(flag));
				}

				result.Page = page;
				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Resolves a message flag with a resolution status and note. Department admins only.
		/// </summary>
		/// <param name="flagId">Chat message flag identifier</param>
		/// <param name="input">Resolution status and note</param>
		/// <returns>ChatActionResult indicating whether the flag was resolved</returns>
		[HttpPut("ResolveFlag")]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> ResolveFlag(string flagId, [FromBody] ResolveFlagInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(flagId))
				return BadRequest();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var result = new ChatActionResult();
			ChatMessageFlag resolved;

			try
			{
				resolved = await _chatModerationService.ResolveFlagAsync(flagId, DepartmentId, UserId, (ChatFlagStatus)input.Resolution, input.ResolutionNote, cancellationToken, BuildModerationContext("DepartmentAdmin"));
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			result.Success = resolved != null;
			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Flags

		#region Moderator Actions

		/// <summary>
		/// Deletes a message as a moderator (tombstone delete with audit). Requires channel moderator rights.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <param name="reason">Reason for the deletion</param>
		/// <returns>ChatActionResult indicating whether the message was deleted</returns>
		[HttpDelete("DeleteMessage")]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> DeleteMessage(string messageId, string reason, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var message = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (message == null || message.DepartmentId != DepartmentId)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(message.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();

			try
			{
				result.Success = await _chatModerationService.ModeratorDeleteMessageAsync(DepartmentId, messageId, UserId, reason, cancellationToken, BuildModerationContext("ChannelModerator"));
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			result.Status = result.Success ? ResponseHelper.Deleted : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Mutes (or unmutes) a user in a channel. Requires channel moderator rights.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">Target user and mute expiration (null MutedUntil = unmute)</param>
		/// <returns>ChatActionResult indicating whether the mute was applied</returns>
		[HttpPost("MuteUser")]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> MuteUser(string channelId, [FromBody] MuteUserInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(input.TargetUserId))
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();

			try
			{
				result.Success = await _chatModerationService.SetUserMutedAsync(DepartmentId, channelId, input.TargetUserId, input.MutedUntil, UserId, null, cancellationToken, BuildModerationContext("ChannelModerator"));
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Bans (or unbans) a user from a channel. Requires channel moderator rights.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">Target user and whether they are banned</param>
		/// <returns>ChatActionResult indicating whether the ban was applied</returns>
		[HttpPost("BanUser")]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> BanUser(string channelId, [FromBody] BanUserInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(input.TargetUserId))
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();

			try
			{
				result.Success = await _chatModerationService.SetUserBannedAsync(DepartmentId, channelId, input.TargetUserId, input.Banned, UserId, null, cancellationToken, BuildModerationContext("ChannelModerator"));
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Locks (or unlocks) a channel so only moderators can post. Requires channel moderator rights.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">Whether to lock and the reason</param>
		/// <returns>ChatActionResult indicating whether the lock state was changed</returns>
		[HttpPost("LockChannel")]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> LockChannel(string channelId, [FromBody] LockChannelInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null)
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();

			try
			{
				result.Success = await _chatModerationService.SetChannelLockedAsync(DepartmentId, channelId, input.Locked, UserId, input.Reason, cancellationToken, BuildModerationContext("ChannelModerator"));
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns the moderation audit trail for the department (optionally limited to one channel).
		/// Department admins only.
		/// </summary>
		/// <param name="channelId">Optional channel to limit the audit trail to</param>
		/// <param name="page">Page number</param>
		/// <param name="pageSize">Page size</param>
		/// <returns>Array of ChatModerationActionResultData objects</returns>
		[HttpGet("GetActions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatModerationActionsResult>> GetActions(string channelId = null, int page = 0, int pageSize = 50)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var result = new GetChatModerationActionsResult();
			var actions = await _chatModerationService.GetModerationActionsAsync(DepartmentId, UserId, channelId, page, pageSize);

			if (actions != null && actions.Any())
			{
				foreach (var action in actions)
				{
					result.Data.Add(ConvertModerationActionResultData(action));
				}

				result.Page = page;
				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Moderator Actions

		#region Settings

		/// <summary>
		/// Returns the per-department chat settings. Department admins only.
		/// </summary>
		/// <returns>ChatSettingsResultData with the department's chat settings</returns>
		[HttpGet("GetSettings")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatSettingsResult>> GetSettings()
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var result = new GetChatSettingsResult();
			var settings = await _chatChannelService.GetDepartmentSettingsAsync(DepartmentId);

			if (settings != null)
			{
				result.Data = ConvertSettingsResultData(settings);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Updates the per-department chat settings. Department admins only.
		/// </summary>
		/// <param name="input">New settings values</param>
		/// <returns>GetChatSettingsResult with the saved settings</returns>
		[HttpPut("UpdateSettings")]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatSettingsResult>> UpdateSettings([FromBody] UpdateChatSettingsInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null)
				return BadRequest();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(DepartmentId) ?? new ChatDepartmentSetting();
			var beforeJson = settings.CloneJsonToString();

			settings.DepartmentId = DepartmentId;
			settings.RetentionDays = input.RetentionDays;
			settings.AllowImages = input.AllowImages;
			settings.AllowGifs = input.AllowGifs;
			settings.AllowLocationSharing = input.AllowLocationSharing;
			settings.UrgentOverridesMute = input.UrgentOverridesMute;
			settings.MaxAttachmentSizeMb = input.MaxAttachmentSizeMb;
			settings.ChatbotEnabled = input.ChatbotEnabled;

			var result = new GetChatSettingsResult();
			var saved = await _chatChannelService.SaveDepartmentSettingsAsync(DepartmentId, UserId, settings, cancellationToken);

			if (saved != null)
			{
				_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = DepartmentId,
					UserId = UserId,
					Type = AuditLogTypes.ChatSettingsChanged,
					Before = beforeJson,
					After = saved.CloneJsonToString(),
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
				});

				result.Data = ConvertSettingsResultData(saved);
				result.PageSize = 1;
				result.Status = ResponseHelper.Updated;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Settings

		#region Exports

		/// <summary>
		/// Queues a chat transcript export job (records requests / FOIA). Department admins only.
		/// </summary>
		/// <param name="input">Channel, date range and format for the export</param>
		/// <returns>GetChatExportsResult containing the queued export job</returns>
		[HttpPost("RequestExport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatExportsResult>> RequestExport([FromBody] RequestExportInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null)
				return BadRequest();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			// Rule 87: require a recent MFA step-up before releasing PII. Blocks callers without 2FA enrolled.
			var mfaGate = await CheckRecentExportMfaAsync();
			if (mfaGate != null)
				return mfaGate;

			// Per-department rate limit: bulk transcript exports carry PII, so cap how many a department
			// can queue per window to blunt exfiltration/abuse (a compromised admin can't drain the
			// department in a loop). Keyed by department, not user, so it holds across admins.
			if (await IsExportRateLimitedAsync())
			{
				var limited = new GetChatExportsResult { Status = ResponseHelper.Failure };
				ResponseHelper.PopulateV4ResponseData(limited);
				return StatusCode(StatusCodes.Status429TooManyRequests, limited);
			}

			var result = new GetChatExportsResult();
			var export = await _chatModerationService.RequestExportAsync(DepartmentId, UserId, input.ChatChannelId, input.StartDate, input.EndDate, (ChatExportFormat)input.Format, cancellationToken, BuildModerationContext("DepartmentAdmin"));

			if (export != null)
			{
				result.Data.Add(ConvertExportResultData(export));
				result.PageSize = 1;
				result.Status = ResponseHelper.Queued;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Establishes a recent-MFA step-up proof for chat transcript exports. Verifies the caller's current
		/// authenticator (TOTP) code and, on success, records a server-side proof valid for a short window so
		/// a subsequent RequestExport can release PII. Department admins with 2FA enrolled only.
		/// </summary>
		/// <param name="input">The caller's current authenticator (TOTP) code</param>
		/// <returns>ChatActionResult indicating whether the step-up succeeded</returns>
		[HttpPost("VerifyExportMfa")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> VerifyExportMfa([FromBody] VerifyExportMfaInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (input == null || string.IsNullOrWhiteSpace(input.TotpCode))
				return BadRequest();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			// A caller with no 2FA enrolled can never obtain a proof — exports stay blocked until they enroll.
			var (user, mfaError) = await GetMfaEnrolledUserOrErrorAsync();
			if (mfaError != null)
				return mfaError;

			// Brute-force guard: cap TOTP verification attempts per user per window before we ever verify.
			var attempts = await _cacheProvider.IncrementAsync(GetMfaVerifyRateLimitCacheKey(user.Id), MfaVerifyRateLimitWindow);
			if (attempts > MfaVerifyMaxAttemptsPerWindow)
				return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "rate_limited", error_description = "Too many verification attempts. Wait a minute and try again." });

			var valid = await _userManager.VerifyTwoFactorTokenAsync(
				user,
				_userManager.Options.Tokens.AuthenticatorTokenProvider,
				input.TotpCode.Trim());

			var result = new ChatActionResult();

			if (!valid)
			{
				// Feed the Identity failed-access counter so repeated wrong codes escalate to account lockout.
				await _userManager.AccessFailedAsync(user);
				result.Success = false;
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return StatusCode(StatusCodes.Status401Unauthorized, result);
			}

			// Successful step-up clears the failed-access counter (standard post-auth reset) so prior typos
			// don't accumulate toward an account lockout.
			await _userManager.ResetAccessFailedCountAsync(user);

			await _cacheProvider.SetStringAsync(
				GetExportMfaProofCacheKey(user.Id),
				DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
				TimeSpan.FromMinutes(ExportMfaWindowMinutes));

			result.Success = true;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Resolves the current user and enforces the export MFA-enrollment precondition shared by the verify
		/// and gate paths. On success returns the user with a null error; otherwise returns a null user and
		/// the HTTP result to return: 401 when the principal can't be resolved, 403 when 2FA is not enrolled.
		/// </summary>
		private async Task<(Model.Identity.IdentityUser User, ActionResult Error)> GetMfaEnrolledUserOrErrorAsync()
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null)
				return (null, Unauthorized());

			if (!await _userManager.GetTwoFactorEnabledAsync(user))
				return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "mfa_enrollment_required", error_description = "Two-Factor Authentication must be enabled to export chat transcripts." }));

			return (user, null);
		}

		/// <summary>
		/// Enforces the Rule 87 recent-MFA requirement for PII exports. Returns null when the caller may
		/// proceed, or the HTTP result to return otherwise: 403 when 2FA is not enrolled (must enroll before
		/// any export), 401 when no fresh step-up proof exists (must call VerifyExportMfa first).
		/// </summary>
		private async Task<ActionResult<GetChatExportsResult>> CheckRecentExportMfaAsync()
		{
			var (user, mfaError) = await GetMfaEnrolledUserOrErrorAsync();
			if (mfaError != null)
				return mfaError;

			var proof = await _cacheProvider.GetStringAsync(GetExportMfaProofCacheKey(user.Id));
			if (!string.IsNullOrEmpty(proof)
				&& DateTime.TryParse(proof, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var verifiedAt)
				&& DateTime.UtcNow <= verifiedAt.AddMinutes(ExportMfaWindowMinutes))
				return null;

			return StatusCode(StatusCodes.Status401Unauthorized, new { error = "mfa_required", error_description = $"Recent Two-Factor verification is required. Call VerifyExportMfa with your current code, then retry within {ExportMfaWindowMinutes} minutes." });
		}

		/// <summary>
		/// Returns the chat transcript export jobs for the department. Department admins only.
		/// </summary>
		/// <returns>Array of ChatExportResultData objects</returns>
		[HttpGet("GetExports")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatExportsResult>> GetExports()
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var result = new GetChatExportsResult();
			var exports = await _chatModerationService.GetExportsAsync(DepartmentId, UserId);

			if (exports != null && exports.Any())
			{
				foreach (var export in exports)
				{
					result.Data.Add(ConvertExportResultData(export));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Downloads a completed chat transcript export. Department admins only; the download is audited.
		/// </summary>
		/// <param name="exportId">Chat export identifier</param>
		/// <returns>The export file</returns>
		[HttpGet("DownloadExport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> DownloadExport(string exportId, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var export = await _chatModerationService.GetExportForDownloadAsync(exportId, DepartmentId, UserId, cancellationToken, BuildModerationContext("DepartmentAdmin"));

			if (export == null || export.Data == null)
				return NotFound();

			string contentType;
			string extension;

			switch ((ChatExportFormat)export.Format)
			{
				case ChatExportFormat.Json:
					contentType = "application/json";
					extension = "json";
					break;
				case ChatExportFormat.Csv:
					contentType = "text/csv";
					extension = "csv";
					break;
				default:
					contentType = "application/zip";
					extension = "zip";
					break;
			}

			return File(export.Data, contentType, $"chat-export-{exportId}.{extension}");
		}

		#endregion Exports

		#region Private Helpers

		private async Task<bool> ChatEnabledAsync()
		{
			return await _authorizationService.IsUserValidWithinLimitsAsync(UserId, DepartmentId) &&
				await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId);
		}

		/// <summary>
		/// Per-department sliding-window rate limit for transcript exports. Returns true when the
		/// department has exceeded ChatConfig.ExportRateLimitPerWindow for the current window.
		/// </summary>
		private async Task<bool> IsExportRateLimitedAsync()
		{
			if (ChatConfig.ExportRateLimitPerWindow <= 0 || ChatConfig.ExportRateLimitWindowSeconds <= 0)
				return false;

			var count = await _cacheProvider.IncrementAsync($"chat:rl:export:{DepartmentId}", TimeSpan.FromSeconds(ChatConfig.ExportRateLimitWindowSeconds));

			return count > ChatConfig.ExportRateLimitPerWindow;
		}

		/// <summary>
		/// Captures the request's forensic context (ip, user-agent, trace id) for the moderation audit
		/// trail. <paramref name="actorRole"/> records the authority the action was taken under
		/// (department admin vs channel moderator).
		/// </summary>
		private ChatModerationContext BuildModerationContext(string actorRole)
		{
			return new ChatModerationContext
			{
				IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
				UserAgent = Request?.Headers != null ? Request.Headers["User-Agent"].ToString() : null,
				TraceId = HttpContext?.TraceIdentifier,
				ActorRole = actorRole
			};
		}

		private static ChatFlagResultData ConvertFlagResultData(ChatMessageFlag flag)
		{
			return new ChatFlagResultData
			{
				ChatMessageFlagId = flag.ChatMessageFlagId,
				ChatMessageId = flag.ChatMessageId,
				ChatChannelId = flag.ChatChannelId,
				FlaggedByUserId = flag.FlaggedByUserId,
				Reason = flag.Reason,
				Note = flag.Note,
				FlaggedOn = flag.FlaggedOn,
				Status = flag.Status,
				ReviewedByUserId = flag.ReviewedByUserId,
				ReviewedOn = flag.ReviewedOn,
				ResolutionNote = flag.ResolutionNote
			};
		}

		private static ChatModerationActionResultData ConvertModerationActionResultData(ChatModerationAction action)
		{
			return new ChatModerationActionResultData
			{
				ChatModerationActionId = action.ChatModerationActionId,
				ChatChannelId = action.ChatChannelId,
				ChatMessageId = action.ChatMessageId,
				TargetUserId = action.TargetUserId,
				TargetUnitId = action.TargetUnitId,
				ActionType = action.ActionType,
				PerformedByUserId = action.PerformedByUserId,
				PerformedOn = action.PerformedOn,
				Reason = action.Reason,
				DetailsJson = action.DetailsJson
			};
		}

		private static ChatSettingsResultData ConvertSettingsResultData(ChatDepartmentSetting settings)
		{
			return new ChatSettingsResultData
			{
				ChatDepartmentSettingId = settings.ChatDepartmentSettingId,
				RetentionDays = settings.RetentionDays,
				AllowImages = settings.AllowImages,
				AllowGifs = settings.AllowGifs,
				AllowLocationSharing = settings.AllowLocationSharing,
				UrgentOverridesMute = settings.UrgentOverridesMute,
				MaxAttachmentSizeMb = settings.MaxAttachmentSizeMb,
				ChatbotEnabled = settings.ChatbotEnabled
			};
		}

		private static ChatExportResultData ConvertExportResultData(ChatExport export)
		{
			return new ChatExportResultData
			{
				ChatExportId = export.ChatExportId,
				ChatChannelId = export.ChatChannelId,
				RequestedByUserId = export.RequestedByUserId,
				RequestedOn = export.RequestedOn,
				StartDate = export.StartDate,
				EndDate = export.EndDate,
				Format = export.Format,
				Status = export.Status,
				CompletedOn = export.CompletedOn,
				Error = export.Error
			};
		}

		#endregion Private Helpers
	}
}
