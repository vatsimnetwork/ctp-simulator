using CoordinateSharp;
using Newtonsoft.Json;
using static CTPSimulator.JsonWrapping;

namespace CTPSimulator
{
    public class SimulatorCalculationParameters
    {
        // GENERAL

        /// <summary>Calculate and output / helper values that can aid in troubleshooting</summary>
        public bool HighVerbosity { get; set; } = false;


        public enum SlotGenerationMode
        {
            Random,
            MaximizeAirportPairs,
            MaximizeSlots
        }

        [JsonIgnoreOnSerialization]
        public SlotGenerationMode IntendedSlotGenerationMode { get; set; } = SlotGenerationMode.MaximizeSlots;

        /// <summary>Populated by the simulator to be displayed back to the user</summary>
        [JsonOnlyOnCreateSlotDistributionSerialization]
        public List<string> SlotGenerationOutputComments { get; set; } = new();


        // SIMULATION
        [JsonIgnoreOnSerialization]
        public double DepartureTimeWindowOffsetSynchronizationLongitude { get; set; } = -30;

        [JsonIgnoreOnSerialization]
        public uint SimulationAnalysisResolutionInMinutes { get; set; } = 2;

        [JsonIgnoreOnSerialization]
        /// <summary>Should the simulation try to use the weather forecast data of the actual event day (only available about 16 days in advance)?
        /// Warning: Initially loading the forecast data might take a few minutes.
        /// If false or if no forecast data is available, the data set will fall back to statistical average values.</summary>
        public bool ShouldSimulationUseActualWeatherForecastData { get; set; } = false;

        public enum DepartureTimeWindowOffsetsCalculationMode
        {
            None,
            EarliestRoutes,
            LatestRoutes,
            RouteAverage
        }

        [JsonIgnoreOnSerialization]
        public DepartureTimeWindowOffsetsCalculationMode IntendedDepartureTimeWindowOffsetsCalculationMode { get; set; } = DepartureTimeWindowOffsetsCalculationMode.EarliestRoutes;

        [JsonIgnoreOnSerialization]
        public TimeOnly DepartureTimeWindowOffsetSynchronizationTimeOfDay { get; set; } = new TimeOnly(16, 0);

        [JsonIgnoreOnSerialization]
        public bool CalculateThroughputDataOnlyForManuallyProvidedSectors { get; set; } = true;


        public enum WaypointThroughputCalculationMode
        {
            None,
            FirstWaypointsOfNATRouteSegmentsOnly,
            AllWaypoints
        }

        [JsonIgnoreOnSerialization]
        public WaypointThroughputCalculationMode IntendedWaypointThroughputCalculationMode { get; set; } = WaypointThroughputCalculationMode.FirstWaypointsOfNATRouteSegmentsOnly;

        [JsonIgnoreOnSerialization]
        public double ThresholdToCheckIfAirplaneIsCountedAtWaypointInNm = 5d;


        [JsonIgnoreOnSerialization]
        public double CalculationFallbackGroundSpeed { get; set; } = 500d;

        [JsonIgnoreOnSerialization]
        /// <summary>Should we use an ellipsoid earth model for more precise but more performance-hungry distance calculations?</summary>
        public bool HighSimulationAccuracy { get; set; } = true;

        // calculation precision
        [JsonIgnore]
        public Shape SimulationEarthShape => HighSimulationAccuracy ? Shape.Ellipsoid : Shape.Sphere;


        /// <summary>Populated by the simulator to be displayed back to the user</summary>
        [JsonOnlyOnSimulateEventSerialization]
        public List<string> SimulationOutputComments { get; set; } = new();
    }
}
