using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace CTPSimulator
{
    public class VATSIMEvent
    {
        public uint Id { get; set; }

        // values coming from the database
        public string Title { get; set; } = string.Empty; // for example CTP 26E

        public uint RouteRevision { get; set; }
        public uint SlotRevision { get; set; }

        [JsonIgnore]
        public DateOnly Date { get; set; }

        [JsonIgnore]
        public TimeSpan DepartureTimeWindow { get; set; } = TimeSpan.FromHours(3); // how long will departure airports depart for?


        public SimulatorCalculationParameters CalculationParameters { get; set; } = new();


        // througput points
        public List<Airport> Airports { get; set; } = new();

        public List<Location> Waypoints { get; set; } = new();

        public List<RouteSegment> RouteSegments { get; set; } = new();

        public List<Sector> Sectors { get; set; } = new();


        // values populated by the simulator
        [JsonIgnore]
        public List<Airport> DepartureAirports { get; set; } = new();

        [JsonIgnore]
        public List<Airport> ArrivalAirports { get; set; } = new();


        public List<Slot> Slots { get; set; } = new();


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

        [JsonIgnore]
        private DateTime? _synchronizationDateTime;
        [JsonIgnore]
        public DateTime SynchronizationDateTime
        {
            get
            {
                if (!_synchronizationDateTime.HasValue)
                {
                    _synchronizationDateTime = Date.ToDateTime(CalculationParameters.DepartureTimeWindowOffsetSynchronizationTimeOfDay);
                }
                return _synchronizationDateTime.Value;
            }
        }
    }


    public abstract class ThroughputPoint
    {
        // values coming from the database
        public uint Id { get; set; }

        public string Identifier { get; set; } = string.Empty; // for example SPESA or EDDF or "PORTI_BOS_1", or oceanic track "M" or "EHAA" for sectors

        [JsonIgnore]
        public ushort MaximumAircraftPerHour { get; set; } = 20; // default for waypoints and route segments, airports and sectors will override this
        public ushort MaximumSlots { get; set; }


        // values populated by the simulator
        public ushort SlotsAllocated { get; set; }

        [JsonIgnore]
        public Dictionary<int, List<Slot>> SlotsAnalysisFramesViaMinutesFromSynchronizationTimeInternal { get; set; } = new();

        public Dictionary<int, List<uint>> SlotsAnalysisFramesViaMinutesFromSynchronizationTime { get; set; } = new();

        // values / functions only for the simulator internally
        [JsonIgnore]
        public int SlotsStillAvailable => MaximumSlots - SlotsAllocated;

        [JsonIgnore]
        public bool AreSlotsStillAvailable => SlotsAllocated < MaximumSlots;
    }

    public class Location : ThroughputPoint // waypoint or airport
    {
        // values coming from the database
        [JsonIgnore]
        public double Latitude { get; set; }

        [JsonIgnore]
        public double Longitude { get; set; }
    }

    public class Airport : Location
    {
        [JsonIgnore]
        public ushort NumberOfVotes { get; set; }

        // values populated by the simulator
        public DateTime DepartureTimeWindowStart { get; set; }

        [JsonIgnore]
        public List<RouteSegment> ConnectingPrimaryRouteSegments { get; set; } = new();

        [JsonIgnore]
        public List<RouteSegment> ConnectingSecondaryRouteSegments { get; set; } = new();
    }

    public class RouteSegment : ThroughputPoint
    {
        // values coming from the database
        public string RouteString { get; set; } = string.Empty; // for example "MARUN Y150 TOLGI SAS P605 NOLGO" or "RESNO 5520N 5530N 5540N 5550N LOMSI"

        [JsonIgnore]
        public string RouteSegmentGroup { get; set; } = string.Empty; // for example NAT or EMEA
        
        [JsonIgnore]
        public string Color { get; set; } = string.Empty;

        [JsonIgnore]
        public bool Enabled { get; set; } = true;

        [JsonIgnore]
        public List<string> RouteSegmentTags { get; set; } = new();

        [JsonIgnore]
        public List<Sector> ProvidedFacilityProgression { get; set; } = new();

        [JsonIgnore]
        public List<Location> Locations { get; set; } = new(); // can be waypoints or airports

        [JsonIgnore]
        /// <summary>
        /// During which RouteRevision was this route last modified?
        /// </summary>
        public uint RouteRevision { get; set; }

        public void CheckValidity()
        {
            if (Locations.Count < 2) throw new ArgumentException($"Route segment {Identifier} has invalid number of Locations (a minimum of 2 is required).");
        }
    }

    public class Sector : ThroughputPoint
    {
        [JsonIgnore]
        public List<SectorBoundary> SectorBoundaries { get; set; } = new List<SectorBoundary>();
    }

    public class SectorBoundary
    {
        public uint Id { get; set; }
        public double MaxLatitude { get; set; }
        public double MinLatitude { get; set; }
        public double MaxLongitude { get; set; }
        public double MinLongitude { get; set; }
        public double[,] Coordinates { get; set; }
    }

    public class Slot
    {
        public uint Id { get; set; }

        // values populated by the simulator
        [JsonIgnore]
        public List<RouteSegment> RouteSegmentsInternal { get; set; } = new();

        public List<uint> RouteSegments { get; set; } = new();

        public DateTime DepartureTime { get; set; }
        public DateTime ProjectedArrivalTime { get; set; }

        [JsonIgnore]
        public Airport DepartureAirportInternal { get; set; }

        public uint DepartureAirport { get; set; }

        [JsonIgnore]
        public Airport ArrivalAirportInternal { get; set; }

        public uint ArrivalAirport { get; set; }

        [JsonIgnore]
        public TimeSpan TimeUntilSynchronizationLongitudeCrossing { get; set; }

        // a bunch of values must be stored that are outside the scope of the simulator, like
        // CID:
        // Aircraft Type:
        // ...
        // ...
    }
}