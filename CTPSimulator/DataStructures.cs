using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace CTPSimulator
{

    public class VATSIMEvent
    {
        // values coming from the database
        public string Title { get; set; } // for example CTP 26E

        public uint RouteRevision { get; set; }
        public uint SlotRevision { get; set; }


        public DateOnly SynchronizationDate { get; set; }
        public double SynchronizationLongitude { get; set; } = -30;
        public TimeSpan DepartureTimeWindow { get; set; } = TimeSpan.FromHours(3); // how long will departure airports depart for?

        public SimulatorCalculationOptions CalculationOptions { get; set; } = new();


        // througput points
        public List<Airport> Airports { get; set; } = new();
        public List<Location> Waypoints { get; set; } = new();
        public List<RouteSegment> RouteSegments { get; set; } = new();
        public List<Sector> Sectors { get; set; } = new();


        // values populated by the simulator
        [NotMapped]
        public List<Airport> DepartureAirports { get; set; } = new();

        [NotMapped]
        public List<Airport> ArrivalAirports { get; set; } = new();


        public List<Slot> Slots { get; set; } = new();
        public TimeSpan SimulationAnalysisResolution { get; set; } = TimeSpan.FromMinutes(2);


        // values / functions only for the simulator internally
        public void ReCalculateMaximumThroughputPointSlots()
        {
            foreach (var airport in Airports) CalculateMaximumThroughputPointSlots(airport);
            foreach (var waypoint in Waypoints) CalculateMaximumThroughputPointSlots(waypoint);
            foreach (var routeSegment in RouteSegments) CalculateMaximumThroughputPointSlots(routeSegment);
            foreach (var sector in Sectors) CalculateMaximumThroughputPointSlots(sector);
        }
        private void CalculateMaximumThroughputPointSlots(ThroughputPoint throughputPoint)
        {
            throughputPoint.MaximumSlots = (ushort)(throughputPoint.MaximumAircraftPerHour * DepartureTimeWindow.TotalHours);
        }
    }


    public abstract class ThroughputPoint
    {
        // values coming from the database
        public string Identifier { get; set; } // for example SPESA or EDDF or "PORTI_BOS_1", or oceanic track "M" or "EHAA" for sectors

        public ushort MaximumAircraftPerHour { get; set; } = 20; // default for waypoints and route segments, airports and sectors will override this
        public ushort MaximumSlots { get; set; }


        // values populated by the simulator
        public ushort SlotsAllocated { get; set; }
        public DateTime SimulationAnalysisStartTime { get; set; } // for example 12z at the given date with graphical resolution of 10 minutes...
        public List<Slot> SlotsAnalysisFrames { get; set; } // if first element is at 12z, second at 1202z, third at 1204z, etc...


        // values / functions only for the simulator internally
        [NotMapped]
        public int SlotsStillAvailable => MaximumSlots - SlotsAllocated;

        [NotMapped]
        public bool AreSlotsStillAvailable => SlotsAllocated < MaximumSlots;
    }

    public class Location : ThroughputPoint // waypoint or airport
    {
        // values coming from the database
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class Airport : Location
    {
        // values populated by the simulator
        public DateTime DepartureTimeWindowStart { get; set; }

        public ushort NumberOfVotes { get; set; }


        [NotMapped]
        public List<RouteSegment> ConnectingPrimaryRouteSegments { get; set; } = new();

        [NotMapped]
        public List<RouteSegment> ConnectingSecondaryRouteSegments { get; set; } = new();
    }

    public class RouteSegment : ThroughputPoint
    {
        // values coming from the database
        public string RouteString { get; set; } // for example "MARUN Y150 TOLGI SAS P605 NOLGO" or "RESNO 5520N 5530N 5540N 5550N LOMSI"
        public string RouteSegmentGroup { get; set; } // for example NAT or EMEA
        public List<string> RouteSegmentTags { get; set; }
        public List<Sector> ProvidedFacilityProgression { get; set; }
        public List<Location> Locations { get; set; } = new(); // can be waypoints or airports
    }

    public class Sector : ThroughputPoint
    {
        public double MaxLatitude { get; set; }
        public double MinLatitude { get; set; }
        public double MaxLongitude { get; set; }
        public double MinLongitude { get; set; }
        public double[,] Coordinates { get; set; }
    }

    public class Slot
    {
        // values populated by the simulator
        public List<RouteSegment> RouteSegments { get; set; } = new();

        public DateTime DepartureTime { get; set; }
        public DateTime ProjectedArrivalTime { get; set; }

        public Airport DepartureAirport { get; set; }
        public Airport ArrivalAirport { get; set; }


        // a bunch of values must be stored that are outside the scope of the simulator, like
        // CID:
        // Aircraft Type:
        // ...
    }
}
