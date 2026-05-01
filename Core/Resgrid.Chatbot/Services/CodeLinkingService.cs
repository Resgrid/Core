using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Code-based account linking service for platforms without OAuth2 (Telegram, Signal, generic).
	/// Generates short-lived linking codes that users enter in chat to prove ownership.
	/// </summary>
	public class CodeLinkingService
	{
		private readonly IChatbotUserIdentityService _userIdentityService;

		public CodeLinkingService(IChatbotUserIdentityService userIdentityService)
		{
			_userIdentityService = userIdentityService;
		}

		/// <summary>
		/// Generates a 6-character alphanumeric linking code for a Resgrid user.
		/// Codes expire after 15 minutes.
		/// </summary>
		public LinkingCode GenerateCode(string resgridUserId)
		{
			var codeBytes = new byte[4];
			using var rng = RandomNumberGenerator.Create();
			rng.GetBytes(codeBytes);

			// Convert to a 6-character code (base36-like, uppercase alphanumeric)
		// Convert to a 6-character code (base36-like, uppercase alphanumeric)
		var value = (uint)(BitConverter.ToUInt32(codeBytes, 0) % 2176782336); // 36^6
		var code = ToBase36(value).PadLeft(6, '0').ToUpperInvariant();
			return new LinkingCode
			{
				Code = code,
				UserId = resgridUserId,
				CreatedAt = DateTime.UtcNow,
				ExpiresAt = DateTime.UtcNow.AddMinutes(15),
				IsUsed = false
			};
		}

		/// <summary>
		/// Processes a linking code entered by a user on a chat platform.
		/// Returns the linked identity on success.
		/// </summary>
		public async Task<LinkResult> ProcessCodeAsync(
			string code,
			ChatbotPlatform platform,
			string platformUserId,
			string displayName)
		{
			if (string.IsNullOrWhiteSpace(code))
				return LinkResult.Fail("Please provide a linking code.");

			// In production, codes would be stored in the ChatbotLinkingCodes table.
			// Phase 2: Validate the code format. Full persistence in Phase 3.
			code = code.Trim().ToUpperInvariant();

			if (code.Length < 4 || code.Length > 8)
				return LinkResult.Fail("Invalid code format. Codes are 6 characters.");

			// Phase 2: Since we don't have full DB persistence yet, validate code format
			// and link the user directly. The web portal generates codes that are checked here.
			// For now, accept any valid-format code as a placeholder.

			var identity = await _userIdentityService.LinkUserAsync(
				code, // placeholder - would look up user from code DB
				platform,
				platformUserId,
				displayName,
				"code");

			return LinkResult.Ok("Account linked successfully! You can now use all chatbot features.", identity);
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
