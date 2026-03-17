using System;
using System.Collections.Generic;
using System.Text;

namespace CTPSimulator
{
    public class SimulatorCalculationOptions
    {
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
    }
}
