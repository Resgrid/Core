namespace Resgrid.WebCore.Areas.User.Models.Contacts;

public class CallJson
{
	public int CallId { get; set; }
	public string CallNumber { get; set; }
	public string CallName { get; set; }
	public int Priority { get; set; }
	public string PriorityName { get; set; }
	public string PriorityColor { get; set; }
	public string CallNature  { get; set; }
	public string LoggedOn { get; set; }
}
