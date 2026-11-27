using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The Resgrid template library (Protocols P5, decision 5). A clone goes through the library's own import (the same name matching,
	/// validation and draft creation as a file), so every item lands as a draft for the department's review-and-publish pass, with the set's
	/// attribution (source, licence, set version) kept on its version.
	/// </summary>
	public class ProtocolTemplateLibraryService : IProtocolTemplateLibraryService
	{
		private readonly IProtocolLibraryService _library;
		private readonly IEventAggregator _eventAggregator;

		public ProtocolTemplateLibraryService(IProtocolLibraryService library, IEventAggregator eventAggregator)
		{
			_library = library;
			_eventAggregator = eventAggregator;
		}

		public IReadOnlyList<ProtocolExchangeDocument> GetSets() => ProtocolTemplateLibrary.Sets;

		public ProtocolExchangeDocument GetSet(string key) => ProtocolTemplateLibrary.Get(key);

		public IReadOnlyList<ProtocolExchangeDocument> GetSets(string culture) => ProtocolTemplateLibrary.SetsFor(culture);

		public Task<ProtocolImportResult> CloneAsync(ProtocolReader reader, string setKey, IEnumerable<string> codes, CancellationToken cancellationToken = default) =>
			CloneAsync(reader, setKey, codes, null, cancellationToken);

		public async Task<ProtocolImportResult> CloneAsync(ProtocolReader reader, string setKey, IEnumerable<string> codes, string culture, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayAuthor)
				return new ProtocolImportResult { Error = "not_authorized" };

			// Round 9C: the drafts carry the set's text in the cloning author's language (machine-translated variants; English otherwise).
			var set = ProtocolTemplateLibrary.Get(setKey, culture);
			if (set == null)
				return new ProtocolImportResult { Error = "not_found" };

			var wanted = new HashSet<string>((codes ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
			var attribution = ProtocolTemplateLibrary.AttributionFor(set);

			// A copy of the embedded set (the shared instance is never changed), every item carrying the set's attribution.
			var document = ProtocolExchangeDocument.Parse(set.ToJson());
			document.Protocols = document.Protocols.Where(p => wanted.Count == 0 || wanted.Contains(p.Code ?? string.Empty)).ToList();
			if (document.Protocols.Count == 0)
				return new ProtocolImportResult { Error = "nothing_selected" };
			foreach (var item in document.Protocols)
			{
				item.Attribution = attribution;
				// Template content is never licensed content, and a starter has no department configuration.
				item.IsLicensedContent = false;
				item.Triggers = new List<ProtocolExchangeTrigger>();
				item.Audience = new List<ProtocolExchangeAudience>();
			}
			document.AttributionSource = ContentAttributionSources.TemplateLibrary;
			document.AttributionSourceRef = set.Set.Key;

			var result = await _library.ImportAsync(reader, document, cancellationToken);
			if (result.Error == null && result.Created > 0)
			{
				try
				{
					_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
					{
						DepartmentId = reader.DepartmentId,
						UserId = reader.UserId,
						Type = AuditLogTypes.ProtocolTemplateCloned,
						After = JsonConvert.SerializeObject(new { Set = set.Set.Key, set.Set.SetVersion, result.Created, result.CreatedProtocolIds }),
						Successful = true,
						ServerName = Environment.MachineName
					});
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Template clone audit failed.");
				}
			}

			return result;
		}
	}
}
