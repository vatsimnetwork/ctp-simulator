using System;
using System.Collections.Generic;
using System.Text;

namespace CTPSimulator
{
    public class SimulatorCalculationParameters
    {
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
            Random
        }
        public SlotGenerationMode IntendedSlotGenerationMode { get; set; } = SlotGenerationMode.MaximizeSlots;

        /// <summary>Populated by the simulator to be displayed back to the user</summary>
        public string SlotGenerationOutputCommentary { get; set; }


        // SIMULATION
        public double DepartureTimeWindowOffsetSynchronizationLongitude { get; set; } = -30;
        public uint SimulationAnalysisResolutionInMinutes { get; set; } = 2;

        /// <summary>Should the simulation try to use the weather forecast data of the actual event day (only available about 16 days in advance)?
        /// Warning: Initially loading the forecast data might take a few minutes.
        /// If false or if no forecast data is available, the data set will fall back to statistical average values.</summary>
        public bool ShouldSimulationUseActualWeatherForecastData { get; set; }

        /// <summary>Should we use an ellipsoid earth model for more precise but more performance-hungry distance calculations?</summary>
        public bool HighSimulationAccuracy { get; set; }

        /// <summary>Populated by the simulator to be displayed back to the user</summary>
        public string SimulationOutputCommentary { get; set; }
    }
}
