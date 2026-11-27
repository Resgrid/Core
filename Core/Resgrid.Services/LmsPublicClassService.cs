using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Paid public classes (§13 T5 item 2, R-C13; decided §25.4a item 12 "both"): a class session can be listed on the learner portal with
	/// an optional fee per external learner. The roster always exports (learner, organization, attendance, fee) for the department's own
	/// billing; where Business Operations invoicing is active (IBusinessOperationsAccessService.CanUseInvoicingAsync) a session raises one
	/// draft invoice per external learner or per sponsoring organization with that add-on's customers, rate items and invoices. The LMS takes
	/// no payment. Fees live in LmsClassFees, written only here, and each fee is invoiced once (a claim marker guards concurrent runs).
	/// </summary>
	public class LmsPublicClassService : ILmsPublicClassService
	{
		private readonly ILmsPublicClassesRepository _publicClasses;
		private readonly ILmsClassFeesRepository _fees;
		private readonly ILmsExternalLearnersRepository _learners;
		private readonly ILmsClassService _classes;
		private readonly IBusinessOperationsAccessService _businessOps;
		private readonly IInvoicingService _invoicing;
		private readonly IContactsService _contacts;
		private readonly IEventAggregator _events;

		public LmsPublicClassService(ILmsPublicClassesRepository publicClasses, ILmsClassFeesRepository fees, ILmsExternalLearnersRepository learners,
			ILmsClassService classes, IBusinessOperationsAccessService businessOps, IInvoicingService invoicing, IContactsService contacts, IEventAggregator events)
		{
			_publicClasses = publicClasses;
			_fees = fees;
			_learners = learners;
			_classes = classes;
			_businessOps = businessOps;
			_invoicing = invoicing;
			_contacts = contacts;
			_events = events;
		}

		public async Task<LmsPublicClass> GetPublicClassAsync(int departmentId, int lmsClassSessionId)
		{
			var row = departmentId <= 0 ? null : await _publicClasses.GetBySessionAsync(departmentId, lmsClassSessionId);
			return row != null && row.DepartmentId == departmentId ? row : null;
		}

		public async Task<LmsPublicClass> SavePublicClassAsync(int departmentId, int lmsClassSessionId, bool isPublic, decimal? feeAmount, string currency, string rateCardItemId,
			string actingUserId, CancellationToken cancellationToken = default)
		{
			var session = await _classes.GetSessionAsync(departmentId, lmsClassSessionId) ?? throw new InvalidOperationException("lms_class_not_found");
			if (feeAmount.HasValue && (feeAmount.Value < 0m || feeAmount.Value > LmsPublicClass.MaxFee))
				throw new InvalidOperationException("lms_xl_fee_invalid");
			// The fee's currency, else the class's (which carries the department's training currency), else the central default.
			var code = LmsTrainingCurrency.Resolve(currency, session.Currency);
			rateCardItemId = string.IsNullOrWhiteSpace(rateCardItemId) ? null : rateCardItemId.Trim();
			if (rateCardItemId != null && !(await GetRateItemsAsync(departmentId)).Any(i => string.Equals(i.RateCardItemId, rateCardItemId, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("lms_xl_rate_item_invalid");

			var now = DateTime.UtcNow;
			var existing = await GetPublicClassAsync(departmentId, lmsClassSessionId);
			var before = existing == null ? null : JsonConvert.SerializeObject(new { existing.IsPublic, existing.FeeAmount, existing.Currency, existing.RateCardItemId });
			var row = existing ?? new LmsPublicClass { DepartmentId = departmentId, LmsClassSessionId = session.LmsClassSessionId, CreatedOn = now };
			row.IsPublic = isPublic;
			row.FeeAmount = feeAmount.HasValue && feeAmount.Value > 0m ? Math.Round(feeAmount.Value, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
			row.Currency = code;
			row.RateCardItemId = rateCardItemId;
			row.UpdatedOn = now;
			row.UpdatedByUserId = actingUserId;
			var stored = row.LmsPublicClassId > 0 ? await _publicClasses.SaveOrUpdateAsync(row, cancellationToken, true) : await _publicClasses.InsertAsync(row, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsPublicClassChanged, before,
				JsonConvert.SerializeObject(new { session.LmsClassSessionId, stored.IsPublic, stored.FeeAmount, stored.Currency, stored.RateCardItemId }));
			return stored;
		}

		public async Task<LmsClassFee> EnsureFeeAsync(LmsClassEnrollment enrollment, CancellationToken cancellationToken = default)
		{
			if (enrollment == null || !LmsExternalLearnerIds.IsExternal(enrollment.UserId) || enrollment.LmsClassEnrollmentId <= 0)
				return null;
			var existing = await _fees.GetByEnrollmentAsync(enrollment.DepartmentId, enrollment.LmsClassEnrollmentId);
			if (existing != null)
				return existing;
			var publicClass = await GetPublicClassAsync(enrollment.DepartmentId, enrollment.LmsClassSessionId);
			if (publicClass == null || !publicClass.HasFee)
				return null;

			var learner = await _learners.GetByLearnerUserIdAsync(enrollment.DepartmentId, enrollment.UserId);
			var fee = new LmsClassFee
			{
				DepartmentId = enrollment.DepartmentId,
				LmsClassSessionId = enrollment.LmsClassSessionId,
				LmsClassEnrollmentId = enrollment.LmsClassEnrollmentId,
				LearnerUserId = enrollment.UserId,
				Amount = publicClass.FeeAmount.Value,
				Currency = publicClass.Currency,
				SponsorContactId = learner?.SponsorContactId,
				CreatedOn = DateTime.UtcNow
			};
			try
			{
				return await _fees.InsertAsync(fee, cancellationToken, true);
			}
			catch (Exception ex) when (IsDuplicate(ex))
			{
				// Two registrations of the same roster place raced: the other one's fee row stands.
				return await _fees.GetByEnrollmentAsync(enrollment.DepartmentId, enrollment.LmsClassEnrollmentId);
			}
		}

		public async Task<LmsClassFee> SetFeeAsync(int departmentId, int lmsClassEnrollmentId, bool waived, string sponsorContactId, string actingUserId,
			CancellationToken cancellationToken = default)
		{
			var fee = await _fees.GetByEnrollmentAsync(departmentId, lmsClassEnrollmentId);
			if (fee == null || fee.DepartmentId != departmentId)
				throw new InvalidOperationException("lms_xl_fee_not_found");
			if (!string.IsNullOrWhiteSpace(fee.InvoiceId))
				throw new InvalidOperationException("lms_xl_fee_invoiced");
			sponsorContactId = string.IsNullOrWhiteSpace(sponsorContactId) ? null : sponsorContactId.Trim();
			if (sponsorContactId != null && !(await GetCustomersAsync(departmentId)).Any(c => string.Equals(c.ContactId, sponsorContactId, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("lms_xl_customer_invalid");

			var before = JsonConvert.SerializeObject(new { fee.Waived, fee.SponsorContactId });
			fee.Waived = waived;
			fee.SponsorContactId = sponsorContactId;
			fee.UpdatedOn = DateTime.UtcNow;
			var stored = await _fees.SaveOrUpdateAsync(fee, cancellationToken, true);
			Audit(departmentId, actingUserId, AuditLogTypes.LmsPublicClassChanged, before,
				JsonConvert.SerializeObject(new { fee.LmsClassSessionId, fee.LmsClassEnrollmentId, stored.Waived, stored.SponsorContactId }));
			return stored;
		}

		public async Task<LmsClassBillingView> GetBillingAsync(int departmentId, int lmsClassSessionId)
		{
			var detail = await _classes.GetDetailAsync(departmentId, lmsClassSessionId, DateTime.UtcNow);
			if (detail == null)
				return null;

			var view = new LmsClassBillingView { Session = detail.Session, PublicClass = await GetPublicClassAsync(departmentId, lmsClassSessionId) };
			var fees = (await _fees.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassFee>())
				.GroupBy(f => f.LmsClassEnrollmentId).ToDictionary(g => g.Key, g => g.First());
			var learners = (await _learners.GetByLearnerUserIdsAsync(departmentId, detail.Roster.Select(r => r.Enrollment.UserId)) ?? new List<LmsExternalLearner>())
				.GroupBy(l => l.LearnerUserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			view.InvoicingAvailable = await InvoicingAvailableAsync(departmentId);

			foreach (var row in detail.Roster)
			{
				fees.TryGetValue(row.Enrollment.LmsClassEnrollmentId, out var fee);
				if (!row.Enrollment.IsOnRoster && !(fee != null && fee.IsInvoiced) && row.Enrollment.Attendance == (int)LmsClassAttendanceStates.Unmarked)
					continue;
				var external = LmsExternalLearnerIds.IsExternal(row.Enrollment.UserId);
				learners.TryGetValue(row.Enrollment.UserId ?? string.Empty, out var learner);
				view.Rows.Add(new LmsClassBillingRow
				{
					Enrollment = row.Enrollment,
					Learner = learner,
					Fee = fee,
					IsExternal = external,
					Name = learner != null && !string.IsNullOrWhiteSpace(learner.Name) ? learner.Name : row.Name,
					// Members' addresses are not exported: the roster carries what billing needs.
					Email = external ? learner?.Email : null,
					Organization = external ? learner?.Organization ?? row.Enrollment.JobTitle : null,
					Billable = LmsExternalLearnerRules.IsBillable(row.Enrollment, fee)
				});
			}

			if (view.InvoicingAvailable)
			{
				foreach (var invoiceId in view.Rows.Where(r => r.Fee != null && r.Fee.IsInvoiced).Select(r => r.Fee.InvoiceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
				{
					try
					{
						var invoice = await _invoicing.GetInvoiceByIdAsync(invoiceId, departmentId);
						foreach (var row in view.Rows.Where(r => r.Fee != null && string.Equals(r.Fee.InvoiceId, invoiceId, StringComparison.OrdinalIgnoreCase)))
							row.InvoiceLabel = invoice?.DisplayNumber;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, "An invoice of a public class could not be read.");
					}
				}
			}

			return view;
		}

		public async Task<string> ExportRosterCsvAsync(int departmentId, int lmsClassSessionId, Func<string, string> label)
		{
			var view = await GetBillingAsync(departmentId, lmsClassSessionId);
			return view == null ? null : LmsExternalLearnerRules.RosterCsv(view.Rows, label);
		}

		public async Task<List<CustomerBillingProfile>> GetCustomersAsync(int departmentId)
		{
			if (!await InvoicingAvailableAsync(departmentId))
				return new List<CustomerBillingProfile>();
			return (await _invoicing.GetBillingProfilesForDepartmentAsync(departmentId) ?? new List<CustomerBillingProfile>())
				.Where(p => p != null && p.DepartmentId == departmentId && p.Active && !p.IsDeleted).ToList();
		}

		public async Task<List<RateCardItem>> GetRateItemsAsync(int departmentId)
		{
			var items = new List<RateCardItem>();
			if (!await InvoicingAvailableAsync(departmentId))
				return items;
			foreach (var card in (await _invoicing.GetRateCardsForDepartmentAsync(departmentId) ?? new List<RateCard>()).Where(c => c != null && c.Active && !c.IsDeleted))
			{
				var full = await _invoicing.GetRateCardByIdAsync(card.RateCardId, departmentId);
				items.AddRange((full?.Items ?? new List<RateCardItem>()).Where(i => i != null && i.Active && !i.IsDeleted && i.DepartmentId == departmentId));
			}
			return items;
		}

		public async Task<LmsClassInvoiceRun> RaiseInvoicesAsync(int departmentId, int lmsClassSessionId, LmsClassInvoiceModes mode, string actingUserId, string ipAddress,
			string userAgent, CancellationToken cancellationToken = default)
		{
			var run = new LmsClassInvoiceRun();
			if (!await InvoicingAvailableAsync(departmentId))
			{
				run.Error = "lms_xl_invoicing_unavailable";
				return run;
			}

			var view = await GetBillingAsync(departmentId, lmsClassSessionId);
			if (view == null)
			{
				run.Error = "lms_class_not_found";
				return run;
			}

			var (groups, skipped) = LmsExternalLearnerRules.GroupForInvoices(view.Rows, mode);
			run.Skipped.AddRange(skipped.Select(r => new KeyValuePair<string, string>(r.Name, "lms_xl_no_sponsor")));
			var rateItem = view.PublicClass?.RateCardItemId;

			foreach (var group in groups)
			{
				string contactId;
				try
				{
					contactId = mode == LmsClassInvoiceModes.PerOrganization ? group.Key : await EnsureLearnerCustomerAsync(departmentId, group.Value[0].Learner, actingUserId, ipAddress, userAgent, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Logging.LogException(ex, "A customer for an external learner could not be made.");
					contactId = null;
				}
				if (string.IsNullOrWhiteSpace(contactId))
				{
					run.Skipped.AddRange(group.Value.Select(r => new KeyValuePair<string, string>(r.Name, "lms_xl_no_customer")));
					continue;
				}

				// The claim: only rows this run holds are invoiced, so two runs at once never bill a fee twice.
				var claim = LmsClassFee.ClaimPrefix + Guid.NewGuid().ToString("N");
				var ids = group.Value.Select(r => r.Fee.LmsClassFeeId).ToList();
				if (await _fees.ClaimAsync(departmentId, ids, claim, cancellationToken) == 0)
					continue;
				var claimed = (await _fees.GetForSessionAsync(departmentId, lmsClassSessionId) ?? new List<LmsClassFee>())
					.Where(f => string.Equals(f.InvoiceId, claim, StringComparison.Ordinal)).Select(f => f.LmsClassFeeId).ToHashSet();
				var rows = group.Value.Where(r => claimed.Contains(r.Fee.LmsClassFeeId)).ToList();

				try
				{
					var currency = rows.Select(r => r.Fee.Currency).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? view.PublicClass?.Currency;
					var invoice = await _invoicing.CreateDraftInvoiceAsync(departmentId, contactId, actingUserId, ipAddress, userAgent, currency, cancellationToken);
					var lines = rows.Select((r, i) => new InvoiceLineItem
					{
						InvoiceId = invoice.InvoiceId,
						DepartmentId = departmentId,
						RateCardItemId = rateItem,
						Description = LmsExternalLearnerRules.Trim(view.Session.Title + " - " + view.Session.StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " - " + r.Name, 500),
						Quantity = 1m,
						UnitRate = r.Fee.Amount,
						Amount = r.Fee.Amount,
						Taxable = false,
						SortOrder = i
					}).ToList();
					await _invoicing.SaveInvoiceLineItemsAsync(invoice.InvoiceId, departmentId, lines, actingUserId, ipAddress, userAgent, cancellationToken);
					await _fees.CompleteClaimAsync(departmentId, rows.Select(r => r.Fee.LmsClassFeeId), claim, invoice.InvoiceId, DateTime.UtcNow, cancellationToken);
					run.InvoiceIds.Add(invoice.InvoiceId);
					run.LearnersInvoiced += rows.Count;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Logging.LogException(ex, $"An invoice for public class session {lmsClassSessionId} could not be raised.");
					await _fees.ReleaseClaimAsync(departmentId, claim, CancellationToken.None);
					run.Skipped.AddRange(rows.Select(r => new KeyValuePair<string, string>(r.Name, ex is InvalidOperationException && ex.Message == "invoicing_profile_required" ? "lms_xl_no_customer" : "lms_xl_invoice_failed")));
				}
			}

			if (run.InvoiceIds.Count > 0)
				Audit(departmentId, actingUserId, AuditLogTypes.LmsClassInvoicesRaised, null,
					JsonConvert.SerializeObject(new { lmsClassSessionId, mode = mode.ToString(), run.InvoiceIds, run.LearnersInvoiced }), ipAddress, userAgent);
			return run;
		}

		/// <summary>
		/// The learner's own Business Operations customer for a per-learner invoice: the one linked, or a new person contact with a billing
		/// profile made from the learner's name and e-mail (the department's own customer record), then linked.
		/// </summary>
		private async Task<string> EnsureLearnerCustomerAsync(int departmentId, LmsExternalLearner learner, string actingUserId, string ipAddress, string userAgent,
			CancellationToken cancellationToken)
		{
			if (learner == null || learner.Status == (int)LmsExternalLearnerStatuses.Erased)
				return null;
			if (!string.IsNullOrWhiteSpace(learner.BillingContactId) && await _invoicing.GetBillingProfileByContactIdAsync(learner.BillingContactId, departmentId) != null)
				return learner.BillingContactId;

			var now = DateTime.UtcNow;
			var contact = await _contacts.SaveContactAsync(new Contact
			{
				DepartmentId = departmentId,
				ContactType = 0,
				FirstName = learner.FirstName,
				LastName = learner.LastName,
				Email = learner.Email,
				CompanyName = learner.Organization,
				AddedOn = now,
				AddedByUserId = actingUserId
			}, cancellationToken);
			await _invoicing.SaveBillingProfileAsync(new CustomerBillingProfile
			{
				DepartmentId = departmentId,
				ContactId = contact.ContactId,
				BillingEmail = learner.Email,
				UseContactMailingAddress = true,
				TermsNetDays = 30,
				Active = true,
				AddedOn = now,
				AddedByUserId = actingUserId
			}, actingUserId, ipAddress, userAgent, cancellationToken);

			learner.BillingContactId = contact.ContactId;
			learner.UpdatedOn = now;
			// Only that column (security review follow-up, M0412): this copy of the learner row is never written back whole.
			await _learners.SetBillingContactAsync(departmentId, learner.LmsExternalLearnerId, contact.ContactId, now, cancellationToken);
			return contact.ContactId;
		}

		private async Task<bool> InvoicingAvailableAsync(int departmentId)
		{
			try
			{
				return departmentId > 0 && await _businessOps.CanUseInvoicingAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The Business Operations invoicing gate could not be read.");
				return false;
			}
		}

		internal static string NormalizeCurrency(string currency) => LmsTrainingCurrency.Normalize(currency);

		private static bool IsDuplicate(Exception ex)
		{
			for (var e = ex; e != null; e = e.InnerException)
			{
				if (e is Npgsql.PostgresException postgres && postgres.SqlState == "23505")
					return true;
				if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
					return true;
			}
			return false;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after, string ipAddress = null, string userAgent = null)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				IpAddress = ipAddress,
				UserAgent = userAgent,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
