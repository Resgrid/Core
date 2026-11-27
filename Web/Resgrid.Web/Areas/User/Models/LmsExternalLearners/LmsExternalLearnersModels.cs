using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.LmsExternalLearners
{
	// Trainings → External learners (Training T5 item 2). Lms-prefixed: LMS layer types (LmsRulesTests separation guard).

	public abstract class LmsExternalLearnersPage
	{
		public string Message { get; set; }
		public string Error { get; set; }
		public bool CanAuthor { get; set; }

		/// <summary>The portal's branding, which carries the department's time zone: every date on these pages is department-local.</summary>
		public LmsLearnerPortalBranding Branding { get; set; }

		/// <summary>The department's training currency (Programs → Settings) for fees that name none.</summary>
		public string DefaultCurrency { get; set; }

		/// <summary>A stored UTC time in the department's time zone.</summary>
		public DateTime Local(DateTime utc) => Branding?.Local(utc) ?? utc;
	}

	public class LmsExternalLearnersIndexModel : LmsExternalLearnersPage
	{
		public LmsLearnerPortal Portal { get; set; }
		public string PortalUrl { get; set; }
		public LmsFairUse FairUse { get; set; } = new LmsFairUse();
		public List<LmsExternalLearner> Learners { get; set; } = new List<LmsExternalLearner>();
		public List<LmsCourse> Courses { get; set; } = new List<LmsCourse>();
		public List<LmsClassSession> Sessions { get; set; } = new List<LmsClassSession>();
		public List<CustomerBillingProfile> Customers { get; set; } = new List<CustomerBillingProfile>();
		public Dictionary<string, string> CustomerNames { get; set; } = new Dictionary<string, string>();
	}

	public class LmsExternalLearnerModel : LmsExternalLearnersPage
	{
		public LmsExternalLearner Learner { get; set; }
		public LmsLearnerMyLearning Learning { get; set; } = new LmsLearnerMyLearning();
		public List<LmsCourse> Courses { get; set; } = new List<LmsCourse>();
		public List<LmsClassSession> Sessions { get; set; } = new List<LmsClassSession>();
		public List<CustomerBillingProfile> Customers { get; set; } = new List<CustomerBillingProfile>();
		public Dictionary<string, string> CustomerNames { get; set; } = new Dictionary<string, string>();
	}

	public class LmsLearnerPortalSettingsModel : LmsExternalLearnersPage
	{
		public LmsLearnerPortal Portal { get; set; }
		public string PortalUrl { get; set; }
		public LmsLearnerPortalInput Input { get; set; } = new LmsLearnerPortalInput();
	}

	public class LmsExternalCoursesModel : LmsExternalLearnersPage
	{
		public List<LmsCourse> Courses { get; set; } = new List<LmsCourse>();
		public Dictionary<int, int> Access { get; set; } = new Dictionary<int, int>();
	}

	public class LmsPublicClassModel : LmsExternalLearnersPage
	{
		public LmsClassBillingView Billing { get; set; }
		public List<CustomerBillingProfile> Customers { get; set; } = new List<CustomerBillingProfile>();
		public Dictionary<string, string> CustomerNames { get; set; } = new Dictionary<string, string>();
		public List<RateCardItem> RateItems { get; set; } = new List<RateCardItem>();
		public bool CanInvoice { get; set; }
	}
}
