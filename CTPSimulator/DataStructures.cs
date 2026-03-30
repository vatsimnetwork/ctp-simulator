using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using static CTPSimulator.JsonWrapping;

namespace CTPSimulator
{
    public class VATSIMEvent
    {
        public ulong Id { get; set; }

        // values coming from the database
        public string Title { get; set; } = string.Empty; // for example CTP 26E

        public uint RouteRevision { get; set; }
        public uint SlotRevision { get; set; }

        [JsonIgnoreSerialization]
        public DateOnly Date { get; set; }

        [JsonIgnoreSerialization]
        public TimeSpan DepartureTimeWindow { get; set; } = TimeSpan.FromHours(3); // how long will departure airports depart for?


        public SimulatorCalculationParameters CalculationParameters { get; set; } = new();


        // througput points
        [JsonIgnoreCreateSlotDistributionSerialization]
        public List<Airport> Airports { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, Airport> AirportsById = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        public List<Location> Waypoints { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, Location> WaypointsById = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        public List<RouteSegment> RouteSegments { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, RouteSegment> RouteSegmentsById = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        public List<Sector> Sectors { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, Sector> SectorsById = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        public List<TagLimit> TagLimits { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, TagLimit> TagLimitsById = new();


        // values populated by the simulator
        [JsonIgnore]
        public List<Airport> DepartureAirports { get; set; } = new();

        [JsonIgnore]
        public List<Airport> ArrivalAirports { get; set; } = new();


        public List<Slot> Slots { get; set; } = new();


        // values / functions only for the simulator internally
        public void ReCalculateMaximumThroughputPointSlots()
        {
            // Airports: MaximumSlots is provided directly from the DB, do not recalculate
            foreach (var waypoint in Waypoints) CalculateMaximumThroughputPointSlots(waypoint);
            foreach (var routeSegment in RouteSegments) CalculateMaximumThroughputPointSlots(routeSegment);
            // Sectors: MaximumSlots is provided directly (explicit hard limit) or stays 0 (unlimited).
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
        public ulong Id { get; set; }

        public string Identifier { get; set; } = string.Empty; // for example SPESA or EDDF or "PORTI_BOS_1", or oceanic track "M" or "EHAA" for sectors

        [JsonIgnoreSerialization]
        public ushort MaximumAircraftPerHour { get; set; } = 20; // default for waypoints and route segments, airports and sectors will override this

        public ushort MaximumSlots { get; set; }


        // values populated by the simulator
        [JsonIgnore]
        public Dictionary<int, List<Slot>> SlotsAnalysisFramesViaMinutesFromSynchronizationTimeInternal { get; set; } = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        public Dictionary<int, List<ulong>> SlotsAnalysisFramesViaMinutesFromSynchronizationTime { get; set; } = new();

        // values / functions only for the simulator internally
        [JsonIgnore]
        public ushort SlotsAllocated { get; set; }

        [JsonIgnore]
        public int SlotsStillAvailable => MaximumSlots - SlotsAllocated;

        [JsonIgnore]
        public bool AreSlotsStillAvailable => SlotsAllocated < MaximumSlots;
    }

    public class Location : ThroughputPoint // waypoint or airport
    {
        // values coming from the database
        [JsonIgnoreSerialization]
        public double Latitude { get; set; }

        [JsonIgnoreSerialization]
        public double Longitude { get; set; }
    }

    public class Airport : Location
    {
        [JsonIgnoreSerialization]
        public ushort NumberOfVotes { get; set; }

        [JsonIgnoreCreateSlotDistributionSerialization]
        // values populated by the simulator
        public DateTime DepartureTimeWindowStart { get; set; }

        [JsonIgnore]
        public List<RouteSegment> ConnectingPrimaryRouteSegments { get; set; } = new();

        [JsonIgnore]
        public List<RouteSegment> ConnectingSecondaryRouteSegments { get; set; } = new();

        [JsonIgnore]
        public List<Airport> ConnectingAirports { get; set; } = new();
    }




    public class RouteSegment : ThroughputPoint
    {
        // values coming from the database
        [JsonIgnoreSerialization]
        public string RouteString { get; set; } = string.Empty; // for example "MARUN Y150 TOLGI SAS P605 NOLGO" or "RESNO 5520N 5530N 5540N 5550N LOMSI"

        [JsonIgnoreSerialization]
        public string RouteSegmentGroup { get; set; } = string.Empty; // for example NAT or EMEA

        [JsonIgnoreSerialization]
        public string Color { get; set; } = string.Empty;

        [JsonIgnoreSerialization]
        public bool Enabled { get; set; } = true;

        [JsonIgnoreSerialization]
        public List<ulong> RouteSegmentTagIds { get; set; } = new();

        [JsonIgnore]
        public List<TagLimit> RouteSegmentTagLimitsInternal { get; set; } = new();

        [JsonIgnore]
        public List<Sector> ProvidedFacilityProgressionInternal { get; set; } = new();

        [JsonIgnoreSerialization]
        public List<ulong> ProvidedFacilityProgression { get; set; } = new();

        [JsonIgnore]
        public List<Location> LocationsInternal { get; set; } = new(); // can be waypoints or airports

        [JsonIgnoreSerialization]
        public List<ulong> Locations { get; set; } = new();

        [JsonIgnore]
        /// <summary>
        /// During which RouteRevision was this route last modified?
        /// </summary>
        public uint RouteRevision { get; set; }

        public void CheckValidity()
        {
            if (LocationsInternal.Count < 2) throw new ArgumentException($"Route segment {Identifier} has invalid number of Locations (a minimum of 2 is required).");
        }
    }

    public class Sector : ThroughputPoint
    {
        [JsonIgnore]
        public List<SectorBoundary> SectorBoundaries { get; set; } = new List<SectorBoundary>();
    }

    public class TagLimit
    {
        public ulong Id { get; set; }
        public string Tag { get; set; } = string.Empty;
        public ushort MaximumSlots { get; set; }

        [JsonIgnore]
        public ushort SlotsAllocated { get; set; }

        [JsonIgnore]
        public bool AreSlotsStillAvailable => SlotsAllocated < MaximumSlots;
    }

    public class SectorBoundary
    {
        public double MaxLatitude { get; set; }
        public double MinLatitude { get; set; }
        public double MaxLongitude { get; set; }
        public double MinLongitude { get; set; }
        public double[,] Coordinates { get; set; }
    }

    public class Slot
    {
        public ulong Id { get; set; }

        // values populated by the simulator
        [JsonIgnore]
        public List<RouteSegment> RouteSegmentsInternal { get; set; } = new();

        public List<ulong> RouteSegments { get; set; } = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        public DateTime DepartureTime { get; set; }

        [JsonIgnoreCreateSlotDistributionSerialization]
        public DateTime ProjectedArrivalTime { get; set; }

        [JsonIgnore]
        public TimeSpan ProjectedFlightTime => ProjectedArrivalTime - DepartureTime;

        [JsonIgnore]
        public Airport DepartureAirportInternal { get; set; }

        public ulong DepartureAirport { get; set; }

        [JsonIgnore]
        public Airport ArrivalAirportInternal { get; set; }

        public ulong ArrivalAirport { get; set; }

        [JsonIgnore]
        public TimeSpan TimeUntilSynchronizationLongitudeCrossing { get; set; }

        // a bunch of values must be stored that are outside the scope of the simulator, like
        // CID:
        // Aircraft Type:
        // ...
        // ...
    }
}