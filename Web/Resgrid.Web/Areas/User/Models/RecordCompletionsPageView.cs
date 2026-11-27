using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models
{
	/// <summary>
	/// Round 7B (permission 135): the "Record completion" page of a baseline training (Trainings) or an LMS course (Courses), from its assignee
	/// or enrolment list. Baseline view types only (the LMS page fills it from the LMS service).
	/// </summary>
	public class RecordCompletionsPageView
	{
		public TrainingCompletionRecordingView View { get; set; }
		/// <summary>"Trainings" or "Courses": the controller the forms post to (RecordCompletions, VoidRecordedCompletion with targetId).</summary>
		public string Controller { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Func<DateTime, DateTime> Local { get; set; } = d => d;
		public string Message { get; set; }
		public string Error { get; set; }

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;
	}
}
