using CoordinateSharp;
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

        [JsonOnlyOnHighVerbositySerialization]
        public DateOnly Date { get; set; }

        [JsonOnlyOnHighVerbositySerialization]
        public TimeSpan DepartureTimeWindow { get; set; } = TimeSpan.FromHours(3); // how long will departure airports depart for?


        public SimulatorCalculationParameters CalculationParameters { get; set; } = new();


        // throughput points
        [JsonOnlyOnSimulateEventSerialization]
        public List<Airport> Airports { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, Airport> AirportsById = new();

        [JsonOnlyOnSimulateEventSerialization]
        public List<Location> Waypoints { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, Location> WaypointsById = new();

        [JsonOnlyOnSimulateEventSerialization]
        public List<RouteSegment> RouteSegments { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, RouteSegment> RouteSegmentsById = new();

        [JsonOnlyOnSimulateEventSerialization]
        public List<Sector> Sectors { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, Sector> SectorsById = new();

        [JsonOnlyOnSimulateEventSerialization]
        public List<ThroughputPoint> TagLimits { get; set; } = new();

        [JsonIgnore]
        public Dictionary<ulong, ThroughputPoint> TagLimitsById = new();


        // values populated by the simulator
        [JsonIgnore]
        public List<Airport> DepartureAirports { get; set; } = new();

        [JsonIgnore]
        public List<Airport> ArrivalAirports { get; set; } = new();


        public List<Slot> Slots { get; set; } = new();


        // values / functions only for the simulator internally
        [JsonIgnore]
        private DateTimeOffset? _synchronizationDateTime;
        [JsonOnlyOnHighVerbositySerialization]
        public DateTimeOffset SynchronizationDateTime
        {
            get
            {
                if (!_synchronizationDateTime.HasValue)
                {
                    _synchronizationDateTime = Date.ToDateTime(CalculationParameters.DepartureTimeWindowOffsetSynchronizationTimeOfDay, DateTimeKind.Utc);
                }
                return _synchronizationDateTime.Value;
            }
        }
    }


    public class ThroughputPoint
    {
        public const int InfinityMarker = 65535;

        // values coming from the database
        public ulong Id { get; set; }

        public string Identifier { get; set; } = string.Empty; // for example SPESA or EDDF or "PORTI_BOS_1", or oceanic track "M" or "EHAA" for sectors

        [JsonIgnoreOnSerialization]
        public ushort MaximumAircraftPerHour { get; set; } = 20; // default for waypoints and route segments, airports and sectors will override this

        public ushort MaximumSlots { get; set; }


        // values populated by the simulator
        [JsonIgnore]
        public Dictionary<int, List<Slot>> AnalysisFramesViaMinutesFromSynchronizationTimeSlots { get; set; } = new();

        [JsonOnlyOnSimulateEventSerialization]
        public Dictionary<int, List<ulong>> AnalysisFramesViaMinutesFromSynchronizationTimeSlotIds { get; set; } = new();

        // values / functions only for the simulator internally
        [JsonIgnore]
        public ushort SlotsAllocated { get; set; }

        [JsonIgnore]
        public int SlotsStillAvailable => MaximumSlots >= InfinityMarker ? int.MaxValue : MaximumSlots - SlotsAllocated;

        [JsonIgnore]
        public bool AreSlotsStillAvailable => MaximumSlots >= InfinityMarker || SlotsAllocated < MaximumSlots;
    }

    public class Location : ThroughputPoint // waypoint or airport
    {
        // values coming from the database
        [JsonIgnoreOnSerialization]
        public double Latitude { get; set; }

        [JsonIgnoreOnSerialization]
        public double Longitude { get; set; }
    }

    public class Airport : Location
    {
        [JsonIgnoreOnSerialization]
        public ushort NumberOfVotes { get; set; }

        [JsonOnlyOnSimulateEventSerialization]
        // values populated by the simulator
        public DateTimeOffset DepartureTimeWindowStart { get; set; }

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
        [JsonIgnoreOnSerialization]
        public string RouteString { get; set; } = string.Empty; // for example "MARUN Y150 TOLGI SAS P605 NOLGO" or "RESNO 5520N 5530N 5540N 5550N LOMSI"

        [JsonIgnoreOnSerialization]
        public string Group { get; set; } = string.Empty; // for example NAT or EMEA

        [JsonIgnoreOnSerialization]
        public string Color { get; set; } = string.Empty;

        [JsonIgnoreOnSerialization]
        public bool Enabled { get; set; } = true;

        [JsonIgnoreOnSerialization]
        public List<ulong> TagLimitIds { get; set; } = new();

        [JsonIgnore]
        public List<ThroughputPoint> TagLimits { get; set; } = new();

        [JsonIgnore]
        public List<Sector> ProvidedFacilityProgression { get; set; } = new();

        [JsonIgnoreOnSerialization]
        public List<ulong> ProvidedFacilityProgressionIds { get; set; } = new();

        [JsonIgnore]
        public List<Location> Locations { get; set; } = new(); // can be waypoints or airports

        [JsonIgnoreOnSerialization]
        public List<ulong> LocationIds { get; set; } = new();

        [JsonIgnore]
        /// <summary>
        /// During which RouteRevision was this route last modified?
        /// </summary>
        public uint RouteRevision { get; set; }


        // slots per airport pair helpers
        [JsonIgnore]
        Dictionary<(Airport, Airport), uint> NumberOfSlotsPerAirportPair { get; set; } = new();

        public void AddToNumberOfSlotsPerAirportPair(Airport departure, Airport arrival)
        {
            var key = (departure, arrival);
            if (NumberOfSlotsPerAirportPair.ContainsKey(key)) NumberOfSlotsPerAirportPair[key]++;
            else NumberOfSlotsPerAirportPair[key] = 1;
        }
        public uint GetNumberOfSlotsPerAirportPair(Airport departure, Airport arrival) => NumberOfSlotsPerAirportPair.TryGetValue((departure, arrival), out var slots) ? slots : 0;

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
        public List<RouteSegment> RouteSegments { get; set; } = new();

        public List<ulong> RouteSegmentIds { get; set; } = new();

        [JsonOnlyOnSimulateEventSerialization]
        public DateTimeOffset DepartureTime { get; set; }

        [JsonOnlyOnSimulateEventSerialization]
        public DateTimeOffset ProjectedArrivalTime { get; set; }

        [JsonOnlyOnSimulateEventSerialization]
        /// <summary>The positions of this airplane as [Lat, Lon] pairs. The first position is starting {VATSIMEvent.CalculationParameters.SimulationAnalysisResolutionInMinutes} minutes after the {Slot.DepartureTime} and then
        /// each new entry is spaced apart every {SimulationAnalysisResolutionInMinutes} minutes until the plane arrives.</summary>
        public List<double[]> SimulatedPositions { get; set; } = new();

        [JsonOnlyOnHighVerbositySerialization]
        public TimeSpan ProjectedFlightTime => ProjectedArrivalTime - DepartureTime;

        [JsonIgnore]
        public Airport DepartureAirport { get; set; }

        public ulong DepartureAirportId { get; set; }

        [JsonIgnore]
        public Airport ArrivalAirport { get; set; }

        public ulong ArrivalAirportId { get; set; }



        [JsonIgnore]
        public bool HasBeenSetupForEnrouteCalculations { get; set; }

        [JsonIgnore]
        public List<(Location, Coordinate, RouteSegment)> RouteWaypoints { get; set; }

        [JsonIgnore]
        public TimeSpan TimeUntilSynchronizationLongitudeCrossing { get; set; }

        [JsonOnlyOnHighVerbositySerialization, JsonOnlyOnSimulateEventSerialization]
        public double RoutingDistanceInNm { get; set; } = 0;

        [JsonOnlyOnHighVerbositySerialization]
        public string CombinedRouteString { get; set; } = string.Empty;

        /// <summary>Distnaces between each waypoint</summary>
        [JsonOnlyOnHighVerbositySerialization, JsonOnlyOnSimulateEventSerialization]
        public List<string> EnrouteDistances { get; set; } = new();

        // a bunch of values must be stored that are outside the scope of the simulator, like
        // CID:
        // Aircraft Type:
        // ...
        // ...
    }
}