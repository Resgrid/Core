using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Chatbot.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Repositories;
using LinkingCodeEntity = Resgrid.Model.ChatbotLinkingCode;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Code-based account linking service for platforms without OAuth2 (Telegram, Signal, generic).
	/// Generates short-lived, single-use linking codes (persisted in ChatbotLinkingCodes) that users
	/// enter in chat to prove ownership of their Resgrid account.
	/// </summary>
	public class CodeLinkingService
	{
		private readonly IChatbotUserIdentityService _userIdentityService;
		private readonly IChatbotLinkingCodeRepository _linkingCodeRepository;

		public CodeLinkingService(
			IChatbotUserIdentityService userIdentityService,
			IChatbotLinkingCodeRepository linkingCodeRepository)
		{
			_userIdentityService = userIdentityService;
			_linkingCodeRepository = linkingCodeRepository;
		}

		/// <summary>
		/// Generates a single-use linking code bound to a Resgrid user and persists it until it
		/// expires. Enforces the per-user daily generation cap.
		/// </summary>
		public async Task<LinkingCode> GenerateCodeAsync(string resgridUserId)
		{
			if (string.IsNullOrWhiteSpace(resgridUserId))
				throw new ArgumentException("A user id is required to generate a linking code.", nameof(resgridUserId));

			// Enforce the per-user daily cap.
			var since = DateTime.UtcNow.AddDays(-1);
			var existing = await _linkingCodeRepository.GetAllByUserIdAsync(resgridUserId);
			var recent = existing?.Count(c => c.CreatedAt >= since) ?? 0;
			if (recent >= ChatbotConfig.MaxLinkingCodesPerUserPerDay)
				throw new InvalidOperationException("You've reached the daily limit for linking codes. Please try again later.");

			// Generate a unique code (retry on the rare collision with an existing code).
			string code;
			var attempts = 0;
			do
			{
				code = GenerateRandomCode();
				attempts++;
			}
			while (await _linkingCodeRepository.GetByCodeAsync(code) != null && attempts < 10);

			var entity = new LinkingCodeEntity
			{
				Id = Guid.NewGuid().ToString("N"),
				UserId = resgridUserId,
				Code = code,
				IsUsed = false,
				CreatedAt = DateTime.UtcNow,
				ExpiresAt = DateTime.UtcNow.AddMinutes(ChatbotConfig.LinkingCodeExpiryMinutes)
			};

			await _linkingCodeRepository.InsertAsync(entity, CancellationToken.None);

			return new LinkingCode
			{
				Code = entity.Code,
				UserId = entity.UserId,
				CreatedAt = entity.CreatedAt,
				ExpiresAt = entity.ExpiresAt,
				IsUsed = entity.IsUsed
			};
		}

		/// <summary>
		/// Processes a linking code entered by a user on a chat platform. Validates the code exists,
		/// is unexpired and unused, links the <b>stored</b> Resgrid user (never the code string), and
		/// consumes the code so it cannot be reused.
		/// </summary>
		public async Task<LinkResult> ProcessCodeAsync(
			string code,
			ChatbotPlatform platform,
			string platformUserId,
			string displayName)
		{
			if (string.IsNullOrWhiteSpace(code))
				return LinkResult.Fail("Please provide a linking code.");

			code = code.Trim().ToUpperInvariant();

			var entity = await _linkingCodeRepository.GetByCodeAsync(code);
			if (entity == null)
				return LinkResult.Fail("That linking code is invalid or has expired.");

			if (entity.IsUsed)
				return LinkResult.Fail("That linking code has already been used.");

			if (DateTime.UtcNow > entity.ExpiresAt)
				return LinkResult.Fail("That linking code has expired. Please generate a new one.");

			// Consume the code (single use) before linking so a retry can't reuse it.
			entity.IsUsed = true;
			entity.UsedAt = DateTime.UtcNow;
			entity.Platform = (int)platform;
			entity.PlatformUserId = platformUserId;
			await _linkingCodeRepository.UpdateAsync(entity, CancellationToken.None);

			// Link the platform identity to the Resgrid user the code was issued to.
			var identity = await _userIdentityService.LinkUserAsync(
				entity.UserId,
				platform,
				platformUserId,
				displayName,
				"code",
				code);

			return LinkResult.Ok("Account linked successfully! You can now use all chatbot features.", identity);
		}

		private static string GenerateRandomCode()
		{
			var codeBytes = new byte[4];
			using var rng = RandomNumberGenerator.Create();
			rng.GetBytes(codeBytes);
			var value = (uint)(BitConverter.ToUInt32(codeBytes, 0) % 2176782336); // 36^6
			return ToBase36(value).PadLeft(6, '0').ToUpperInvariant();
		}

		private static string ToBase36(uint value)
		{
			const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
			if (value == 0) return "0";
			var result = "";
			while (value > 0)
			{
				result = chars[(int)(value % 36)] + result;
				value /= 36;
			}
			return result;
		}
	}

	public class LinkingCode
	{
		public string Code { get; set; }
		public string UserId { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime ExpiresAt { get; set; }
		public bool IsUsed { get; set; }
	}
}
