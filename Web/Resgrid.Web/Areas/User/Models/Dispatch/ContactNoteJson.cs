namespace Resgrid.WebCore.Areas.User.Models.Dispatch
{
	public class ContactNoteJson
	{
		public string Id { get; set; }
		public string ContactId { get; set; }
		public string TypeName { get; set; }
		public string TypeColor { get; set; }
		public string Note { get; set; }
		public bool ShouldAlert { get; set; }
		public string AddedOn { get; set; }
		public string AddedBy { get; set; }
	}
}
