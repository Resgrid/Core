namespace GoogleMapsApi.Entities.Places.Request
{
	/// <summary>
	/// Specifies the order in which results are listed
	/// </summary>
	public enum RankBy
	{
		/// <summary>
		/// This option sorts results based on their importance. Ranking will favor prominent places within the specified area. Prominence can be affected by a Place's ranking in Google's index, the number of check-ins from your application, global popularity, and other factors.
		/// </summary>
		Prominence,
		/// <summary>
		/// This option sorts results in ascending order by their distance from the specified location. A radius should not be supplied, and bounds is not supported. One or more of keyword, name, or types is required.
		/// </summary>
		Distance
	}
}