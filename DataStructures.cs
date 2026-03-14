using System;
using System.Collections.Generic;
using System.Text;

namespace CTPSimulator
{
    public class Event
    {
        // values coming from the database
        public string Title { get; set; } // for example CTP 26E

        public ushort RouteRevision { get; set; }
        public ushort SlotRevision { get; set; }

        public DateTime SynchronizationDateTime { get; set; } // the time (in UTC) where the middle of the event is (!)
        public double SynchronizationLongitude { get; set; }
        public TimeSpan DepartureTimeWindow { get; set; } = TimeSpan.FromHours(3); // how long will departure airports depart for?

        public Dictionary<Guid, Location> Waypoints { get; set; }
        public Dictionary<Guid, Airport> Airports { get; set; }
        public Dictionary<string, RouteSegment> RouteSegments { get; set; }


        // values populated by the simulator
        public List<Slot> Slots { get; set; }
        public TimeSpan SimulationTimeResolution { get; set; } = TimeSpan.FromMinutes(2);


        // values / functions only for the simulator internally
        public static Event Current;
        public ushort ConvertToMaximumSlots(ushort maximumAircraftPerHours) => (ushort)(maximumAircraftPerHours * DepartureTimeWindow.TotalHours);
    }

    public abstract class ThroughputPoint
    {
        // values coming from the database
        public ushort MaximumAircraftPerHour { get; set; } = 20; // default value for waypoints and route segments, airports will override this


        // values populated by the simulator
        public ushort SlotsAllocated { get; set; }


        // values / functions only for the simulator internally
        public ushort MaximumSlots => Event.Current.ConvertToMaximumSlots(MaximumAircraftPerHour);
        public bool SlotsStillAvailable => SlotsAllocated < MaximumSlots;
    }

    public class Location : ThroughputPoint // for waypoint or airport
    {
        // values coming from the database
        public string Identifier { get; set; } // for example SPESA or EDDF
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        // values populated by the simulator
        public DateTime ThroughputAnalysisStartTime { get; set; } // for example 12z at the given date with graphical resolution of 10 minutes...
        public List<Slot> NumberOfAircraftWithinSimulatorTimeFrame { get; set; } // if first element is at 12z, second at 1202z, third at 1204z, etc...

        // values / functions only for the simulator internally
    }

    public class Airport : Location
    {
        // values populated by the simulator
        public DateTime DepartureTimeWindowStart { get; set; }
    }

    public class RouteSegment : ThroughputPoint
    {
        // values coming from the database
        public string Identifier { get; set; } // for example "PORTI_BOS_1", or oceanic track "M"
        public string RouteString { get; set; } // for example "MARUN Y150 TOLGI SAS P605 NOLGO" or "RESNO 5520N 5530N 5540N 5550N LOMSI"
        public List<Location> Locations { get; set; } // can be waypoints or airports

        // values / functions only for the simulator internally
        public IEnumerable<RouteSegment> PotentiallyConnectingTo => Event.Current.RouteSegments.Values.Where(x => x.Locations.First() == Locations.Last());
    }

    public class Slot
    {
        // values populated by the simulator
        public List<RouteSegment> RouteSegments { get; set; }

        public DateTime DepartureTime { get; set; }
        public DateTime ProjectedArrivalTime { get; set; }

        public Airport DepartureAirport { get; set; }
        public Airport ArrivalAirport { get; set; }


        // a bunch of values must be stored that are outside the scope of my simulator, like
        // CID:
        // Aircraft Type:
        // ...
    }
}
