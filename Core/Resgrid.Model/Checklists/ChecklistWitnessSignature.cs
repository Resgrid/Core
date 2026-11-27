using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Checklists
{
	/// <summary>
	/// The content a checklist witness signature (S3) binds to: the completion's operational content only (ADP rule, memory
	/// adp-operational-data-rule): which run of which form version on which target, by whom and when, the score and outcome, each
	/// item's status, typed value and failure, the location and the evidence files' hashes. Never the completion note, the witness
	/// attestation, item notes, not-applicable reasons or a free-text, photo or signature answer's value — those are ADP-private —
	/// so the hash is the same for every reader and recomputes without a protected-data grant.
	/// </summary>
	public static class ChecklistWitnessArtifact
	{
		public const string Kind = "checklist-witness/1";

		/// <summary>The canonical artifact text (exposed for tests: it must carry no private member).</summary>
		public static string Canonical(ChecklistCompletion completion, ChecklistForm form, IEnumerable<ChecklistAnswer> answers, IEnumerable<ChecklistCompletionItem> items,
			IEnumerable<ChecklistCompletionFile> files, ChecklistRunInput input)
		{
			if (completion == null)
				throw new ArgumentNullException(nameof(completion));

			var types = (form?.Sections ?? new List<ChecklistSection>()).SelectMany(s => s.Items ?? new List<ChecklistItem>())
				.Where(i => Guid.TryParse(i.Id, out _)).GroupBy(i => Guid.Parse(i.Id).ToString("D"))
				.ToDictionary(g => g.Key, g => g.First().Type, StringComparer.OrdinalIgnoreCase);
			var failures = (items ?? Enumerable.Empty<ChecklistCompletionItem>()).Where(i => !string.IsNullOrWhiteSpace(i.ItemId))
				.GroupBy(i => Normalize(i.ItemId)).ToDictionary(g => g.Key, g => g.First().IsFailure, StringComparer.OrdinalIgnoreCase);

			var artifact = new
			{
				kind = Kind,
				completion = Normalize(completion.Id),
				department = completion.DepartmentId,
				definition = Normalize(completion.ParentId),
				version = Normalize(completion.VersionId),
				target = new { type = completion.TargetType, id = completion.TargetId },
				author = (completion.CreatedBy ?? string.Empty).ToLowerInvariant(),
				submitted = completion.SubmittedOn?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				score = completion.Score?.ToString("0.####", CultureInfo.InvariantCulture),
				passed = completion.Passed,
				location = input == null ? null : new
				{
					description = input.LocationDescription,
					latitude = input.Latitude?.ToString("0.######", CultureInfo.InvariantCulture),
					longitude = input.Longitude?.ToString("0.######", CultureInfo.InvariantCulture)
				},
				answers = (answers ?? Enumerable.Empty<ChecklistAnswer>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.ItemId))
					.Select(a => new { id = Normalize(a.ItemId), answer = a })
					.OrderBy(a => a.id, StringComparer.Ordinal)
					.Select(a => new
					{
						item = a.id,
						status = (int)a.answer.Status,
						// Only a typed item's value (pass/fail, reading, choice...) is operational; free text, photo and signature answers stay out.
						value = types.TryGetValue(a.id, out var type) && ChecklistTables.IsTypedAnswerType(type) ? a.answer.Value : null,
						failure = failures.TryGetValue(a.id, out var failed) ? failed : null
					}).ToList(),
				files = (files ?? Enumerable.Empty<ChecklistCompletionFile>()).Where(f => f != null).OrderBy(f => f.Id, StringComparer.Ordinal)
					.Select(f => new { id = Normalize(f.Id), item = Normalize(f.ItemId), sha256 = (f.Sha256 ?? string.Empty).ToLowerInvariant() }).ToList()
			};

			return JsonConvert.SerializeObject(artifact, Formatting.None);
		}

		public static string Hash(ChecklistCompletion completion, ChecklistForm form, IEnumerable<ChecklistAnswer> answers, IEnumerable<ChecklistCompletionItem> items,
			IEnumerable<ChecklistCompletionFile> files, ChecklistRunInput input) =>
			SignaturePolicy.HashArtifact(Canonical(completion, form, answers, items, files, input));

		private static string Normalize(string id) => Guid.TryParse(id, out var g) ? g.ToString("D") : (id ?? string.Empty).Trim().ToLowerInvariant();
	}

	/// <summary>What the completion page and the API show of a checklist witness signature (S3).</summary>
	public sealed class ChecklistWitnessSignatureView
	{
		/// <summary>The form requires the witness to sign at L2.</summary>
		public bool Required { get; set; }
		public bool AwaitingWitness { get; set; }
		/// <summary>The member may sign now (a different member than the author, the run awaits its witness).</summary>
		public bool CanSign { get; set; }
		public string SubmissionHash { get; set; }
		/// <summary>The completion's operational content hash, recomputed now.</summary>
		public string ArtifactSha256 { get; set; }
		public string Statement { get; set; }
		public List<SignatureVerification> Signatures { get; set; } = new List<SignatureVerification>();
	}
}
