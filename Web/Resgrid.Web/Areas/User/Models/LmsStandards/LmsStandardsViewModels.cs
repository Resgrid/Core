using System;
using System.Collections.Generic;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.LmsStandards
{
	// Trainings → Standards (Training T5, LMS add-on): view models of LmsStandardsController and its view components. LMS layer (§17.3).

	/// <summary>What every Standards page shows: the gate and the one-time notice.</summary>
	public abstract class LmsStandardsPageView
	{
		/// <summary>The add-on is active: packages, tools and the LRS may be changed (lapse rule 3).</summary>
		public bool CanAuthor { get; set; }
		public bool IsLapsed { get; set; }
		public bool IsManager { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public Func<DateTime?, string> When { get; set; } = d => d?.ToString("g");
	}

	public sealed class LmsStandardsPackagesView : LmsStandardsPageView
	{
		public bool StoreConfigured { get; set; }
		public List<LmsContentPackage> Packages { get; set; } = new List<LmsContentPackage>();
		public long MaxUploadBytes { get; set; }
	}

	public sealed class LmsStandardsToolsView : LmsStandardsPageView
	{
		public List<LmsLtiTool> Tools { get; set; } = new List<LmsLtiTool>();
		public LmsLtiPlatformDetails Platform { get; set; }
	}

	public sealed class LmsStandardsToolView : LmsStandardsPageView
	{
		public LmsLtiTool Tool { get; set; }
		public LmsLtiPlatformDetails Platform { get; set; }
	}

	public sealed class LmsStandardsXapiView : LmsStandardsPageView
	{
		public LmsXapiOverview Overview { get; set; }
	}

	/// <summary>The lesson editor's fields of a Scorm, Cmi5 or LtiLink lesson (view component).</summary>
	public sealed class LmsStandardsLessonFieldsView
	{
		public LmsLesson Lesson { get; set; }
		public int Kind { get; set; }
		public List<LmsContentPackage> Packages { get; set; } = new List<LmsContentPackage>();
		public List<LmsLtiTool> Tools { get; set; } = new List<LmsLtiTool>();
	}

	/// <summary>The player's frame (SCORM / cmi5) or launch button (LTI) of a standards lesson (view component).</summary>
	public sealed class LmsStandardsPlayerView
	{
		public int EnrollmentId { get; set; }
		public LmsLesson Lesson { get; set; }
		public LmsContentLaunch Launch { get; set; }
		public LmsLtiTool Tool { get; set; }
		public LmsRuntimeRegistration Registration { get; set; }
		public bool Completed { get; set; }
		public LmsErrors Error { get; set; }
	}
}
