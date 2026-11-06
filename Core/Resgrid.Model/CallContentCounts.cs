namespace Resgrid.Model
{
	/// <summary>
	/// How many notes and attachments one call has, by attachment type: the counts a v4 call list shows, read without loading the
	/// rows (an attachment row carries its file). Counts every row the call's note and file lists return.
	/// </summary>
	public class CallContentCounts
	{
		public int CallId { get; set; }

		/// <summary>Notes on the call.</summary>
		public int NoteCount { get; set; }

		/// <summary>Dispatch audio attachments (<see cref="CallAttachmentTypes.DispatchAudio"/>).</summary>
		public int AudioCount { get; set; }

		/// <summary>Image attachments (<see cref="CallAttachmentTypes.Image"/>).</summary>
		public int ImageCount { get; set; }

		/// <summary>File attachments (<see cref="CallAttachmentTypes.File"/>).</summary>
		public int FileCount { get; set; }
	}
}
