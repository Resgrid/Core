namespace Resgrid.Model
{
	public enum MapIconTypes
	{
		// Begin Call Icons
		Search, //search.png
		Blast, //blast.png
		CarAccident, //caraccident.png
		CrimeScene, //crimescene.png
		Earthquake, //earthquake.png
		EmergencyPhone, //emergencyphone.png
		Fire, //fire.png
		FirstAid, //firstaid.png
		Flood, //flood.png
		Tools, //tools.png
		LineDown, //linedown.png
		Industry, //industry.png
		PowerOutage, //poweroutage.png
		Radiation, //radiation.png
		Shooting, //shooting.png
		Poison, //poison.png
		Gathering, //gathering.png
		TreeDown, //treedown.png
		Worksite, //worksite.png
		Workshop, //workshop.png
		// End Call Icons

		// Begin Unit Icons
		Aircraft, //aircraft.png
		Ambulance, //ambulance.png
		Bulldozer, //bulldozer.png
		Bus, //bus.png
		Car, //car.png
		CarTwo, //car2.png
		Check, //check.png
		Flag, //flag.png
		FourByFour, //fourbyfour.png
		Group, //group.png
		Helicopter, //helicopter.png
		Motorcycle, //motorcycle.png
		Pickup, //pickup.png
		Camper, //camper.png
		Plowtruck, //plowtruck.png
		Tires, //tires.png
		Truck, //truck.png
		Van, //van.png
		Velocimeter, //velocimeter.png
		Watercraft //watercraft.png
		// End Unit Icons
	}

	public class MapIcons
	{
		public static string ConvertTypeToName(MapIconTypes type)
		{
			switch (type)
			{
				case MapIconTypes.Search:
					return "search";
				case MapIconTypes.Blast:
					return "";
				case MapIconTypes.CarAccident:
					return "";
				case MapIconTypes.CrimeScene:
					return "";
				case MapIconTypes.Earthquake:
					return "";
				case MapIconTypes.EmergencyPhone:
					return "";
				case MapIconTypes.Fire:
					return "";
				case MapIconTypes.FirstAid:
					return "";
				case MapIconTypes.Flood:
					return "";
				case MapIconTypes.Tools:
					return "";
				case MapIconTypes.LineDown:
					return "";
				case MapIconTypes.Industry:
					return "";
				case MapIconTypes.PowerOutage:
					return "";
				case MapIconTypes.Radiation:
					return "";
				case MapIconTypes.Shooting:
					return "";
				case MapIconTypes.Poison:
					return "";
				case MapIconTypes.Gathering:
					return "";
				case MapIconTypes.TreeDown:
					return "";
				case MapIconTypes.Worksite:
					return "";
				case MapIconTypes.Workshop:
					return "";
				case MapIconTypes.Aircraft:
					return "";
				case MapIconTypes.Ambulance:
					return "";
				case MapIconTypes.Bulldozer:
					return "";
				case MapIconTypes.Bus:
					return "";
				case MapIconTypes.Car:
					return "";
				case MapIconTypes.CarTwo:
					return "";
				case MapIconTypes.Check:
					return "";
				case MapIconTypes.Flag:
					return "";
				case MapIconTypes.FourByFour:
					return "";
				case MapIconTypes.Group:
					return "";
				case MapIconTypes.Helicopter:
					return "";
				case MapIconTypes.Motorcycle:
					return "";
				case MapIconTypes.Pickup:
					return "";
				case MapIconTypes.Camper:
					return "";
				case MapIconTypes.Plowtruck:
					return "";
				case MapIconTypes.Tires:
					return "";
				case MapIconTypes.Truck:
					return "";
				case MapIconTypes.Van:
					return "";
				case MapIconTypes.Velocimeter:
					return "";
				case MapIconTypes.Watercraft:
					return "";
			}

			return "";
		}
	}
}
