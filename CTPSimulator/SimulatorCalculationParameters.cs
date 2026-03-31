using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using static CTPSimulator.JsonWrapping;

namespace CTPSimulator
{
    public class SimulatorCalculationParameters
    {
        [JsonIgnoreSerialization]
        // SLOT GENERATION
        /// <summary>Should the SlotDistributionCreator recalculate the MaximumSlots of all ThroughputPoints (Airports, RouteSegments, Waypoints, Sectors)
        /// based on the MaximumAircraftPerHour or leave them as they are? (May have been manually overridden)</summary>
        public bool RecalculateMaximumAirportSlots { get; set; } = true;


        public enum SlotGenerationMode
        {
            /// <summary>
            /// Try to squeeze as many slots out of the given set of airports and routes (fully deterministic)
            /// </summary>
            MaximizeSlots,

            /// <summary>
            /// Random distribution
            /// </summary>
            Random,

            /// <summary>
            /// Distribute slots proportionally to airport vote counts, then fill remaining capacity greedily
            /// </summary>
            VoteProportional
        }

        [JsonIgnoreSerialization]
        public SlotGenerationMode IntendedSlotGenerationMode { get; set; } = SlotGenerationMode.MaximizeSlots;

        [JsonIgnore]
        public List<string> SlotGenerationOutputComments { get; set; } = new();

        [JsonIgnoreSimulateEventSerialization]
        /// <summary>Populated by the simulator to be displayed back to the user</summary>
        public string SlotGenerationOutputCommentary { get; set; } = string.Empty;


        // SIMULATION
        [JsonIgnoreSerialization]
        public double DepartureTimeWindowOffsetSynchronizationLongitude { get; set; } = -30;

        [JsonIgnoreSerialization]
        public uint SimulationAnalysisResolutionInMinutes { get; set; } = 2;

        [JsonIgnoreSerialization]
        /// <summary>Should the simulation try to use the weather forecast data of the actual event day (only available about 16 days in advance)?
        /// Warning: Initially loading the forecast data might take a few minutes.
        /// If false or if no forecast data is available, the data set will fall back to statistical average values.</summary>
        public bool ShouldSimulationUseActualWeatherForecastData { get; set; }

        public enum DepartureTimeWindowOffsetsCalculationMode
        {
            None,
            EarliestRoutes,
            LatestRoutes,
            RouteAverage
        }

        [JsonIgnoreSerialization]
        public DepartureTimeWindowOffsetsCalculationMode IntendedDepartureTimeWindowOffsetsCalculationMode { get; set; } = DepartureTimeWindowOffsetsCalculationMode.EarliestRoutes;

        [JsonIgnoreSerialization]
        public TimeOnly DepartureTimeWindowOffsetSynchronizationTimeOfDay { get; set; } = new TimeOnly(16, 0);

        [JsonIgnoreSerialization]
        public bool CalculateThroughputDataOnlyForManuallyProvidedSectors { get; set; } = true;


        public enum WaypointThroughputCalculationMode
        {
            None,
            FirstWaypointsOfNATRouteSegmentsOnly,
            AllWaypoints
        }

        [JsonIgnoreSerialization]
        public WaypointThroughputCalculationMode IntendedWaypointThroughputCalculationMode { get; set; } = WaypointThroughputCalculationMode.FirstWaypointsOfNATRouteSegmentsOnly;

        [JsonIgnoreSerialization]
        public double ThresholdToCheckIfAirplaneIsCountedAtWaypointInNm = 5d;


        [JsonIgnoreSerialization]
        public double CalculationFallbackGroundSpeed { get; set; } = 300d;

        [JsonIgnoreSerialization]
        /// <summary>Should we use an ellipsoid earth model for more precise but more performance-hungry distance calculations?</summary>
        public bool HighSimulationAccuracy { get; set; }


        [JsonIgnore]
        public List<string> SimulationOutputComments { get; set; } = new();

        [JsonIgnoreCreateSlotDistributionSerialization]
        /// <summary>Populated by the simulator to be displayed back to the user</summary>
        public string SimulationOutputCommentary { get; set; } = string.Empty;
    }
}
