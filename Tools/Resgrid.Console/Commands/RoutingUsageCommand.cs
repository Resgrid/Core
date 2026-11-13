using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Console.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Console.Commands
{
	/// <summary>
	///     Prints the routing call counters (<see cref="IRoutingUsageService" />): calls per provider and day, the current
	///     UTC month so far with a full-month projection, the busiest day per provider (what a daily quota such as
	///     SimpleRouting's has to cover), and the Mapbox bill those numbers imply at today's list prices.
	/// </summary>
	public sealed class RoutingUsageCommand(
		ILogger<RoutingUsageCommand> logger,
		IRoutingUsageService routingUsageService) : ICommandService
	{
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			try
			{
				if (!SystemBehaviorConfig.CacheEnabled)
					logger.LogWarning("SystemBehaviorConfig.CacheEnabled is false: routing calls are counted in Redis, so nothing is recorded or readable without it.");

				var days = 31;
				var daysValue = GetValue(args, "Days");
				if (daysValue != null && (!int.TryParse(daysValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out days) || days < 1 || days > 400))
				{
					logger.LogError("--Days must be a whole number from 1 to 400.");
					return ExitCode.Failed;
				}

				var today = DateTime.UtcNow.Date;
				var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
				var from = today.AddDays(1 - days);
				if (monthStart < from)
					from = monthStart;

				var rows = await routingUsageService.GetUsageAsync(from, today, cancellationToken);

				logger.LogInformation($"Routing provider: {MappingConfig.RoutingProvider}, fallback: {MappingConfig.RoutingFallbackProvider}");
				logger.LogInformation("");
				logger.LogInformation($"Daily calls, last {days} days (UTC). Sent = reached the provider; Not sent = answered without calling it.");
				logger.LogInformation("A provider marked * is departments' own accounts (Mapping Settings), billed to them, not the install.");
				logger.LogInformation($"{"Day",-10} {"Provider",-10} {"Operation",-9} {"Sent",9} {"OK",9} {"Rejected",9} {"Failed",9} {"Not sent",9} {"Elements",10}");

				var daily = rows.Where(r => r.Day >= today.AddDays(1 - days)).OrderBy(r => r.Day).ThenBy(r => r.Provider).ThenBy(r => r.DepartmentAccount).ThenBy(r => r.Operation).ToList();
				if (daily.Count == 0)
					logger.LogInformation("(no routing calls counted)");

				foreach (var row in daily)
					logger.LogInformation($"{row.Day:yyyy-MM-dd} {Label(row.Provider, row.DepartmentAccount),-10} {row.Operation,-9} {row.Sent,9:N0} {row.Succeeded,9:N0} {row.Rejected,9:N0} {row.Failed,9:N0} {row.NotSent,9:N0} {row.Elements,10:N0}");

				var month = rows.Where(r => r.Day >= monthStart).ToList();
				var daysElapsed = (today - monthStart).Days + 1;
				var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

				logger.LogInformation("");
				logger.LogInformation($"{monthStart:MMMM yyyy} so far ({daysElapsed} of {daysInMonth} days), projected to the full month:");
				logger.LogInformation($"{"Provider",-10} {"Operation",-9} {"Sent",10} {"Elements",11} {"Projected",11} {"Peak day calls",15}");

				foreach (var group in month.GroupBy(r => new { r.Provider, r.DepartmentAccount, r.Operation })
					         .OrderBy(g => g.Key.Provider).ThenBy(g => g.Key.DepartmentAccount).ThenBy(g => g.Key.Operation))
				{
					var sent = group.Sum(r => r.Sent);
					var elements = group.Sum(r => r.Elements);
					var units = group.Sum(r => r.BillableUnits);
					var projected = Project(units, daysElapsed, daysInMonth);
					var peak = group.Max(r => r.Sent);
					logger.LogInformation($"{Label(group.Key.Provider, group.Key.DepartmentAccount),-10} {group.Key.Operation,-9} {sent,10:N0} {elements,11:N0} {projected,11:N0} {peak,15:N0}");
				}

				// The install's accounts only: a daily quota (SimpleRouting) and the Mapbox bill are the install's.
				var install = month.Where(r => !r.DepartmentAccount).ToList();
				foreach (var provider in install.Select(r => r.Provider).Distinct().OrderBy(p => p))
				{
					var peakDay = install.Where(r => r.Provider == provider).GroupBy(r => r.Day).Select(g => new { Day = g.Key, Sent = g.Sum(r => r.Sent) }).OrderByDescending(d => d.Sent).First();
					logger.LogInformation($"Busiest day for {provider}: {peakDay.Day:yyyy-MM-dd}, {peakDay.Sent:N0} calls (route, matrix and optimize each count as one call against a per-call quota).");
				}

				var mapbox = install.Where(r => r.Provider == RoutingProviderNames.Mapbox).ToList();
				logger.LogInformation("");
				logger.LogInformation("Mapbox list prices (https://www.mapbox.com/pricing, 2026-11-12): 100,000 free per API per month, then $2.00 / $1.60 / $1.20 per 1,000.");
				logger.LogInformation("Counts every call that reached Mapbox, so this is an upper bound if Mapbox does not bill failed calls.");

				var total = 0m;
				var projectedTotal = 0m;
				foreach (var (operation, api, unit) in new[]
				         {
					         (RoutingUsageOperation.Route, "Directions", "requests"),
					         (RoutingUsageOperation.Matrix, "Matrix", "elements"),
					         (RoutingUsageOperation.Optimize, "Optimization", "requests")
				         })
				{
					var units = mapbox.Where(r => r.Operation == operation).Sum(r => r.BillableUnits);
					var projected = Project(units, daysElapsed, daysInMonth);
					var cost = MapboxRoutingPricing.MonthlyCost(units);
					var projectedCost = MapboxRoutingPricing.MonthlyCost(projected);
					total += cost;
					projectedTotal += projectedCost;
					logger.LogInformation($"{api,-12} {units,10:N0} {unit,-8} so far ${cost,9:N2}   projected {projected,10:N0} = ${projectedCost,9:N2}");
				}

				logger.LogInformation($"{"Mapbox total",-12} {"",19} so far ${total,9:N2}   projected {"",10}   ${projectedTotal,9:N2}");
				return ExitCode.Success;
			}
			catch (Exception ex)
			{
				logger.LogError("Reading routing usage failed");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}
		}

		private static string Label(string provider, bool departmentAccount)
		{
			return departmentAccount ? provider + "*" : provider;
		}

		private static long Project(long units, int daysElapsed, int daysInMonth)
		{
			return daysElapsed <= 0 ? units : (long)Math.Round(units * (double)daysInMonth / daysElapsed);
		}

		private static string GetValue(IEnumerable<string> args, string name)
		{
			var prefix = "--" + name + "=";
			var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			return arg?.Substring(prefix.Length);
		}
	}
}
