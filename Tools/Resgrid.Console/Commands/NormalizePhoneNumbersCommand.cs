using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Console.Commands
{
	/// <summary>
	///     One-off sweep that rewrites stored profile phone numbers into the canonical E.164 form the
	///     save flow already produces (+12015550123).
	///     <para>
	///     Most existing rows were written before the phone validation in EditUserProfile/AddPerson
	///     existed, so they hold whatever the user typed - "(270) 555-0101", "0740 055 5012". Inbound SMS
	///     and voice resolve the sender by comparing against the stored number, and those formats match
	///     nothing, so those users cannot be identified by a text or a call. Every current write path
	///     validates and stores E.164, so this only has to run once.
	///     </para>
	///     <para>
	///     Dry run by default: it reports what it would change and writes nothing. Pass --Apply to
	///     commit. Scope to a single department with --DepartmentId=N.
	///     </para>
	/// </summary>
	public sealed class NormalizePhoneNumbersCommand(
		ILogger<NormalizePhoneNumbersCommand> logger,
		IDepartmentsService departmentsService,
		IUserProfilesRepository userProfilesRepository,
		IUserProfileService userProfileService,
		IAddressService addressService,
		IPhoneNumberProcesserProvider phoneNumberProcesser) : ICommandService
	{
		private const string MobileField = "MobileNumber";
		private const string HomeField = "HomeNumber";

		private sealed record Change(int DepartmentId, string UserId, string Field, string From, string To);

		private sealed record Skip(int DepartmentId, string UserId, string Field, string Value, string Reason);

		/// <summary>One stored number in flight: what it was, and what it parsed to (if anything).</summary>
		private sealed class Candidate
		{
			public UserProfile Profile { get; init; }
			public string Field { get; init; }
			public string Original { get; init; }
			public PhoneNumberResult Result { get; set; }

			public bool Parsed => Result != null && Result.IsValid &&
								  !string.IsNullOrWhiteSpace(Result.InternationalNumber);
		}

		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			var apply = args.Any(a => a.Equals("--Apply", StringComparison.OrdinalIgnoreCase));
			var departmentFilter = ParseDepartmentId(args);

			logger.LogInformation("Resgrid Phone Number Normalization");
			logger.LogInformation(apply
				? "Mode: APPLY - matching profiles will be updated."
				: "Mode: DRY RUN - nothing will be written. Pass --Apply to commit.");

			try
			{
				var departments = await departmentsService.GetAllAsync();

				if (departments == null || departments.Count == 0)
				{
					logger.LogWarning("No departments found, nothing to do.");
					return ExitCode.Success;
				}

				if (departmentFilter.HasValue)
				{
					departments = departments.Where(d => d.DepartmentId == departmentFilter.Value).ToList();

					if (departments.Count == 0)
					{
						logger.LogError("Department {DepartmentId} was not found.", departmentFilter.Value);
						return ExitCode.Failed;
					}
				}

				var changes = new List<Change>();
				var skips = new List<Skip>();

				// A profile belongs to a user, not a department, so the same row comes back under every
				// department the user belongs to. Track what has been handled so it is not re-parsed and
				// re-written once per membership, and so a profile is not reported as a failure under one
				// department when it already resolved under another.
				var handled = new HashSet<int>();
				var scanned = 0;

				foreach (var department in departments)
				{
					cancellationToken.ThrowIfCancellationRequested();

					// Includes disabled and deleted members: their rows are still matched by an inbound
					// lookup, so leaving them unnormalized leaves the same identification hole open.
					var profiles = await userProfilesRepository
						.GetAllUserProfilesForDepartmentIncDisabledDeletedAsync(department.DepartmentId);

					if (profiles == null)
						continue;

					var fresh = profiles.Where(p => p != null && !handled.Contains(p.UserProfileId)).ToList();

					if (fresh.Count == 0)
						continue;

					scanned += fresh.Count;

					var candidates = await BuildCandidatesAsync(fresh, department);
					var inferred = InferRegion(candidates);

					if (inferred != null)
						RetryFailuresWithRegion(candidates, inferred);

					var pending = new List<UserProfile>();

					foreach (var profile in fresh)
					{
						var changed = false;

						foreach (var candidate in candidates.Where(c => c.Profile.UserProfileId == profile.UserProfileId))
						{
							if (!candidate.Parsed)
							{
								skips.Add(new Skip(department.DepartmentId, profile.UserId, candidate.Field,
									candidate.Original, ClassifySkip(candidate.Original)));
								continue;
							}

							if (string.Equals(candidate.Result.InternationalNumber, candidate.Original, StringComparison.Ordinal))
								continue;

							changes.Add(new Change(department.DepartmentId, profile.UserId, candidate.Field,
								candidate.Original, candidate.Result.InternationalNumber));

							if (candidate.Field == MobileField)
								profile.MobileNumber = candidate.Result.InternationalNumber;
							else
								profile.HomeNumber = candidate.Result.InternationalNumber;

							changed = true;
						}

						handled.Add(profile.UserProfileId);

						if (!changed)
							continue;

						profile.LastUpdated = DateTime.UtcNow;
						pending.Add(profile);
					}

					if (pending.Count == 0)
						continue;

					if (apply)
					{
						await userProfilesRepository.UpdatePhoneNumbersAsync(pending, cancellationToken);

						// The repository write bypasses the service, so evict the profile caches by hand.
						// Profiles are cached for 14 days - without this the old value stays live.
						foreach (var updated in pending)
							userProfileService.ClearUserProfileFromCache(updated.UserId);

						userProfileService.ClearAllUserProfilesFromCache(department.DepartmentId);
					}

					logger.LogInformation("Department {DepartmentId} ({Name}){Region}: {Count} profile(s) {Action}.",
						department.DepartmentId, department.Name,
						inferred == null ? string.Empty : $" [region {inferred}]",
						pending.Count, apply ? "updated" : "would be updated");
				}

				Report(scanned, changes, skips, apply);
			}
			catch (OperationCanceledException)
			{
				logger.LogWarning("Cancelled. Any department already committed stays committed.");
				return ExitCode.Failed;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "There was an error running the phone number normalization");
				logger.LogError(ex, "Phone number normalization failed.");
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}

		private async Task<List<Candidate>> BuildCandidatesAsync(List<UserProfile> profiles, Department department)
		{
			// Resolved once per department rather than per profile: it is the same lookup for everyone.
			// Both region lookups rethrow: the region decides how a number parses, so swallowing a
			// failed lookup here would let an --Apply run rewrite numbers against the wrong region or
			// report them as unparseable. The outer handler turns this into a non-zero exit.
			string departmentRegion;

			try
			{
				departmentRegion = await CountryIsoAsync(department.AddressId);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to resolve the region for department {DepartmentId} (AddressId {AddressId}).",
					department.DepartmentId, department.AddressId);
				throw;
			}

			var candidates = new List<Candidate>();

			foreach (var profile in profiles)
			{
				string region;

				try
				{
					region = await ResolveRegionAsync(profile, departmentRegion);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to resolve the region for user {UserId} in department {DepartmentId}.",
						profile.UserId, department.DepartmentId);
					throw;
				}

				foreach (var field in new[] { MobileField, HomeField })
				{
					var number = field == MobileField ? profile.MobileNumber : profile.HomeNumber;

					if (string.IsNullOrWhiteSpace(number))
						continue;

					candidates.Add(new Candidate
					{
						Profile = profile,
						Field = field,
						Original = number,
						Result = phoneNumberProcesser.Process(number, region)
					});
				}
			}

			return candidates;
		}

		/// <summary>
		///     The country a department's numbers actually belong to, learned from the ones that already
		///     parsed.
		///     <para>
		///     A national-format number ("07400555012", "0491570156") cannot be read without knowing its
		///     country, and most departments have no address on file to supply one - which is why the
		///     bulk of the skipped rows are perfectly good non-US numbers. Members of a department
		///     overwhelmingly share a country, and rows already stored in E.164 state theirs explicitly,
		///     so the numbers that did parse tell us how to read the ones that did not.
		///     </para>
		///     <para>
		///     Requires a clear majority, so a handful of foreign numbers cannot relabel a department and
		///     turn a bad parse into a confidently wrong number.
		///     </para>
		/// </summary>
		private static string InferRegion(List<Candidate> candidates)
		{
			var regions = candidates
				.Where(c => c.Parsed && !string.IsNullOrWhiteSpace(c.Result.Region))
				.Select(c => c.Result.Region)
				.ToList();

			if (regions.Count == 0)
				return null;

			var top = regions.GroupBy(r => r).OrderByDescending(g => g.Count()).First();

			return top.Count() * 2 > regions.Count ? top.Key : null;
		}

		private void RetryFailuresWithRegion(List<Candidate> candidates, string region)
		{
			foreach (var candidate in candidates.Where(c => !c.Parsed))
			{
				var retry = phoneNumberProcesser.Process(candidate.Original, region);

				if (retry != null && retry.IsValid && !string.IsNullOrWhiteSpace(retry.InternationalNumber))
					candidate.Result = retry;
			}
		}

		/// <summary>
		///     Why a value could not be used, so the report can be triaged in groups rather than read row
		///     by row. Most of what lands here is not a mistyped number at all - it is a name, an email
		///     address, "N/A", or a placeholder - and those want clearing, not fixing.
		/// </summary>
		private static string ClassifySkip(string value)
		{
			var trimmed = (value ?? string.Empty).Trim();
			var digits = trimmed.Count(char.IsDigit);

			if (digits == 0)
				return "not a phone number (no digits)";

			if (trimmed.Contains('@') || trimmed.Any(char.IsLetter))
				return "contains letters";

			if (trimmed.Contains(';') || trimmed.Contains(','))
				return "more than one number in the field";

			if (digits < 7)
				return "too short";

			if (trimmed.Where(char.IsDigit).Distinct().Count() <= 2)
				return "placeholder";

			return "does not parse to a valid number";
		}

		/// <summary>
		///     The country to interpret a national-format number against. Without one, a stored
		///     "0740 055 5012" cannot be resolved to a country code at all.
		///     <para>
		///     Follows EditUserProfile - the home (physical) address country, then the mailing address -
		///     and falls back to the department's own address when the profile has neither, or when the
		///     two disagree. A member whose physical and mailing addresses sit in different countries
		///     gives no reliable answer on its own, so the department the number was issued under is the
		///     better authority than picking one of the two arbitrarily.
		///     </para>
		/// </summary>
		private async Task<string> ResolveRegionAsync(UserProfile profile, string departmentRegion)
		{
			var physical = await CountryIsoAsync(profile.HomeAddressId);
			var mailing = await CountryIsoAsync(profile.MailingAddressId);

			if (physical != null && mailing != null &&
				!string.Equals(physical, mailing, StringComparison.OrdinalIgnoreCase))
				return departmentRegion;

			return physical ?? mailing ?? departmentRegion;
		}

		private async Task<string> CountryIsoAsync(int? addressId)
		{
			if (!addressId.HasValue)
				return null;

			var address = await addressService.GetAddressByIdAsync(addressId.Value);

			return address == null ? null : PhoneRegionHelper.ToIso(address.Country);
		}

		private static int? ParseDepartmentId(string[] args)
		{
			var argument = args.FirstOrDefault(a => a.StartsWith("--DepartmentId=", StringComparison.OrdinalIgnoreCase));

			if (argument == null)
				return null;

			return int.TryParse(argument.Split('=', 2)[1], out var departmentId) ? departmentId : null;
		}

		private void Report(int scanned, List<Change> changes, List<Skip> skips, bool apply)
		{
			logger.LogInformation("-----------------------------------------");
			logger.LogInformation("Profiles scanned:  {Scanned}", scanned);
			logger.LogInformation("Numbers {Action}: {Count}", apply ? "rewritten" : "to rewrite", changes.Count);
			logger.LogInformation("Numbers skipped:   {Count}", skips.Count);

			foreach (var reason in skips.GroupBy(s => s.Reason).OrderByDescending(g => g.Count()))
				logger.LogInformation("  {Count,6}  {Reason}", reason.Count(), reason.Key);

			// After normalization two profiles can land on the same number - production already holds
			// the same number in two formats on different rows. The inbound lookup prefers a verified
			// profile, but these are worth a human look.
			var collisions = changes
				.Where(c => c.Field == MobileField)
				.GroupBy(c => c.To)
				.Where(g => g.Select(c => c.UserId).Distinct().Count() > 1)
				.ToList();

			if (collisions.Count > 0)
			{
				logger.LogWarning("{Count} mobile number(s) end up on more than one profile:", collisions.Count);

				foreach (var collision in collisions)
					logger.LogWarning("  {Number} -> {UserIds}", collision.Key,
						string.Join(", ", collision.Select(c => c.UserId).Distinct()));
			}

			if (skips.Count > 0)
			{
				var path = Path.Combine(Directory.GetCurrentDirectory(), "phone-normalization-skipped.csv");
				var lines = new List<string> { "DepartmentId,UserId,Field,Value,Reason" };

				lines.AddRange(skips.Select(s => string.Join(",",
					s.DepartmentId.ToString(),
					Csv(s.UserId),
					Csv(s.Field),
					Csv(s.Value),
					Csv(s.Reason))));

				System.IO.File.WriteAllLines(path, lines);
				logger.LogInformation("Skipped numbers written to {Path} for review.", path);
			}

			if (!apply && changes.Count > 0)
				logger.LogInformation("Re-run with --Apply to commit these changes.");
		}

		private static string Csv(string value)
		{
			if (string.IsNullOrEmpty(value))
				return "\"\"";

			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}
	}
}
