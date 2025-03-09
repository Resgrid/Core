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
		Watercraft, //watercraft.png
		// End Unit Icons

		// Begin Contact Icons
		Always,
		AboveGround,
		Administration,
		AadministrativeBoundary,
		Apartment,
		ColdStorage,
		CommunityCentre,
		Condominium,
		Conference,
		Congress,
		Court,
		Embassy,
		Expert,
		Job,
		People,
		House,
		Laboratory,
		Key,
		OfficeBuilding,
		Police,
		Postal,
		Townhouse,
		WorkCase,
		Home,
		Adult,
		Family,
		// End Contact Icons
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
					return "blast";
				case MapIconTypes.CarAccident:
					return "caraccident";
				case MapIconTypes.CrimeScene:
					return "crimescene";
				case MapIconTypes.Earthquake:
					return "earthquake";
				case MapIconTypes.EmergencyPhone:
					return "emergencyphone";
				case MapIconTypes.Fire:
					return "fire";
				case MapIconTypes.FirstAid:
					return "firstaid";
				case MapIconTypes.Flood:
					return "flood";
				case MapIconTypes.Tools:
					return "tools";
				case MapIconTypes.LineDown:
					return "linedown";
				case MapIconTypes.Industry:
					return "industry";
				case MapIconTypes.PowerOutage:
					return "poweroutage";
				case MapIconTypes.Radiation:
					return "radiation";
				case MapIconTypes.Shooting:
					return "shooting";
				case MapIconTypes.Poison:
					return "poison";
				case MapIconTypes.Gathering:
					return "gathering";
				case MapIconTypes.TreeDown:
					return "treedown";
				case MapIconTypes.Worksite:
					return "worksite";
				case MapIconTypes.Workshop:
					return "workshop";
				case MapIconTypes.Aircraft:
					return "aircraft";
				case MapIconTypes.Ambulance:
					return "ambulance";
				case MapIconTypes.Bulldozer:
					return "bulldozer";
				case MapIconTypes.Bus:
					return "bus";
				case MapIconTypes.Car:
					return "car";
				case MapIconTypes.CarTwo:
					return "car2";
				case MapIconTypes.Check:
					return "check";
				case MapIconTypes.Flag:
					return "flag";
				case MapIconTypes.FourByFour:
					return "fourbyfour";
				case MapIconTypes.Group:
					return "group";
				case MapIconTypes.Helicopter:
					return "helicopter";
				case MapIconTypes.Motorcycle:
					return "motorcycle";
				case MapIconTypes.Pickup:
					return "pickup";
				case MapIconTypes.Camper:
					return "camper";
				case MapIconTypes.Plowtruck:
					return "plowtruck";
				case MapIconTypes.Tires:
					return "tires";
				case MapIconTypes.Truck:
					return "truck";
				case MapIconTypes.Van:
					return "van";
				case MapIconTypes.Velocimeter:
					return "velocimeter";
				case MapIconTypes.Watercraft:
					return "watercraft";
			}

			return "";
		}
	}
}
