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
	///     existed, so they hold whatever the user typed - "(270) 555-0101", "270-555-0102". Inbound SMS
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
		private sealed record Change(int DepartmentId, string UserId, string Field, string From, string To);

		private sealed record Skip(int DepartmentId, string UserId, string Field, string Value, string Reason);

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

					// Resolved once per department rather than per profile: it is the same lookup for
					// everyone in it.
					var departmentRegion = await CountryIsoAsync(department.AddressId);

					var pending = new List<UserProfile>();

					foreach (var profile in profiles)
					{
						scanned++;

						var region = await ResolveRegionAsync(profile, departmentRegion);

						var mobile = Normalize(profile.MobileNumber, "MobileNumber", region, department.DepartmentId, profile.UserId, skips);
						var home = Normalize(profile.HomeNumber, "HomeNumber", region, department.DepartmentId, profile.UserId, skips);

						if (mobile == null && home == null)
							continue;

						if (mobile != null)
						{
							changes.Add(new Change(department.DepartmentId, profile.UserId, "MobileNumber", profile.MobileNumber, mobile));
							profile.MobileNumber = mobile;
						}

						if (home != null)
						{
							changes.Add(new Change(department.DepartmentId, profile.UserId, "HomeNumber", profile.HomeNumber, home));
							profile.HomeNumber = home;
						}

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

					logger.LogInformation("Department {DepartmentId} ({Name}): {Count} profile(s) {Action}.",
						department.DepartmentId, department.Name, pending.Count, apply ? "updated" : "would be updated");
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

		/// <summary>
		///     Returns the canonical form when the stored value should be rewritten, or null to leave it
		///     alone (blank, already canonical, or not parseable as a real number).
		/// </summary>
		private string Normalize(string number, string field, string region, int departmentId, string userId,
			List<Skip> skips)
		{
			if (string.IsNullOrWhiteSpace(number))
				return null;

			var result = phoneNumberProcesser.Process(number, region);

			if (result == null || !result.IsValid || string.IsNullOrWhiteSpace(result.InternationalNumber))
			{
				// Never guess. A number that does not parse to a real one - a truncated entry, or a
				// national format whose country cannot be resolved from the profile's address - is
				// reported for a human to look at rather than rewritten into something that would
				// dial somewhere else.
				skips.Add(new Skip(departmentId, userId, field, number, "does not parse to a valid number"));
				return null;
			}

			return string.Equals(result.InternationalNumber, number, StringComparison.Ordinal)
				? null
				: result.InternationalNumber;
		}

		/// <summary>
		///     The country to interpret a national-format number against. Without one, a stored
		///     "270-555-0102" cannot be resolved to a country code at all.
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

			// After normalization two profiles can land on the same number - the production data
			// already holds the same number in two formats on different rows. The inbound lookup
			// prefers a verified profile, but these are worth a human look.
			var collisions = changes
				.Where(c => c.Field == "MobileNumber")
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
