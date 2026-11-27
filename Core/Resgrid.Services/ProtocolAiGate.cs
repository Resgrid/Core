using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The gate the protocol AI tools share (round 7A): K1's knowledge entitlement (Enhanced AI with Ai.Knowledge, the self-hosted listing),
	/// failing closed, and the protocol as an author (or approver) may manage it. Never the LMS: Enhanced AI pays for every model call, and
	/// protocol content lives outside the LMS.
	/// </summary>
	public static class ProtocolAiGate
	{
		public static async Task<bool> EntitledAsync(IAiKnowledgeService knowledge, int departmentId)
		{
			if (departmentId <= 0)
				return false;
			try
			{
				return await knowledge.IsEntitledAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The knowledge entitlement could not be read for department {departmentId}.");
				return false;
			}
		}

		/// <summary>The protocol when the member authors (or approves) protocols and may read it, else the refusal.</summary>
		public static async Task<(DispatchProtocol Protocol, ProtocolReader Reader, string Refusal)> AuthorableAsync(IProtocolAccessService access, int departmentId, string userId,
			int protocolId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolId <= 0)
				return (null, null, ProtocolAiOutcomes.Invalid);
			var reader = await access.GetReaderAsync(departmentId, userId);
			if (reader == null || !(reader.MayAuthor || reader.MayApprove))
				return (null, reader, ProtocolAiOutcomes.NotAllowed);
			var protocol = await access.GetReadableAsync(departmentId, userId, protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId || protocol.DispatchProtocolId != protocolId || protocol.IsDeleted)
				return (null, reader, ProtocolAiOutcomes.NotFound);
			return (protocol, reader, null);
		}

		/// <summary>
		/// The protocol's published version in force (published protocol, current version published), else null. A protocol marked "no AI" or
		/// licensed is refused before this is read.
		/// </summary>
		public static async Task<ProtocolVersion> VersionInForceAsync(IProtocolLibraryService library, int departmentId, DispatchProtocol protocol)
		{
			if (protocol == null || protocol.Status != (int)ProtocolStatuses.Published || !protocol.CurrentVersionId.HasValue)
				return null;
			var version = await library.GetVersionAsync(departmentId, protocol.CurrentVersionId.Value);
			return version != null && version.DepartmentId == departmentId && version.DispatchProtocolId == protocol.DispatchProtocolId &&
				   version.Status == (int)ProtocolVersionStatuses.Published
				? version
				: null;
		}
	}

	/// <summary>The protocol AI tools' server-side text (drafted training text) in the author's language (ProtocolAi area, 10 locales).</summary>
	public static class ProtocolAiText
	{
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.ProtocolAi.ProtocolAi", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		/// <summary>The resource in the requested language, else in English, else the key itself (the area ships culture resources only).</summary>
		public static string Get(string key, string language)
		{
			return TryGet(key, Culture(language)) ?? TryGet(key, CultureInfo.GetCultureInfo("en")) ?? key;
		}

		private static CultureInfo Culture(string language)
		{
			try
			{
				return string.IsNullOrWhiteSpace(language) ? CultureInfo.GetCultureInfo("en") : CultureInfo.GetCultureInfo(language.Trim());
			}
			catch (CultureNotFoundException)
			{
				return CultureInfo.GetCultureInfo("en");
			}
		}

		private static string TryGet(string key, CultureInfo culture)
		{
			try
			{
				var value = Strings.GetString(key, culture);
				return string.IsNullOrEmpty(value) ? null : value;
			}
			catch (MissingManifestResourceException)
			{
				return null;
			}
		}
	}
}
