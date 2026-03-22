using CoordinateSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace CTPSimulator
{
    public static class Simulator
    {
        public static async Task SimulateSlot(VATSIMEvent vatsimEvent, DateTime departureTime, Slot slot, bool calculateThroughputs, CancellationToken cancellationToken = default)
        {
            // check validity of data
            if (slot.RouteSegments.Count == 0) throw new ArgumentException($"Invalid number of RouteSegments provided for a slot (a minimum of 1 is required).");
            foreach (var routeSegment in slot.RouteSegments) routeSegment.CheckValidity();

            Location origin = slot.RouteSegments.First().Locations.First();

            // concat locations
            List<(Location, Coordinate)> locations = [(origin, new Coordinate(origin.Latitude, origin.Longitude, new EagerLoad(false)))];
            for (int r = 0; r < slot.RouteSegments.Count; r++)
            {
                for (int l = 1; l < slot.RouteSegments.Count; l++)
                {
                    var location = slot.RouteSegments[r].Locations[l];
                    locations.Add((location, new Coordinate(location.Latitude, location.Longitude, new EagerLoad(false))));
                }
            }

            // calculation precision
            Shape earthShape = vatsimEvent.CalculationParameters.HighSimulationAccuracy ? Shape.Ellipsoid : Shape.Sphere;

            // do the calculations
            DateTime currentTime = departureTime;
            int locationIndex = -1;
            (Location, Coordinate) nextWaypoint;
            bool finalSegment;
            double distanceToNextWaypoint;
            Coordinate currentPosition = new Coordinate(new EagerLoad(false));

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                // shift waypoint
                locationIndex++;
                nextWaypoint = locations[locationIndex + 1];
                finalSegment = locationIndex == locations.Count - 1;
                distanceToNextWaypoint = currentPosition.Get_Distance_From_Coordinate(nextWaypoint.Item2, earthShape).NauticalMiles;
                currentPosition.Latitude = nextWaypoint.Item2.Latitude;
                currentPosition.Longitude = nextWaypoint.Item2.Longitude;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // calculate speed
                    double groundSpeed = 300d; // in knots

                    // calculate current position
                    double timeSliceDistance = groundSpeed * (double)vatsimEvent.CalculationParameters.SimulationAnalysisResolutionInMinutes / 60d; // in nm
                    if (timeSliceDistance < distanceToNextWaypoint)
                    {
                        distanceToNextWaypoint -= timeSliceDistance;
                        currentPosition.Move(nextWaypoint.Item2, new Distance(timeSliceDistance, DistanceType.NauticalMiles), earthShape);
                    }
                    else // we reach the next waypoint within this time slice
                    {
                        if (finalSegment)
                        {
                            slot.ProjectedArrivalTime = currentTime + TimeSpan.FromHours(distanceToNextWaypoint / groundSpeed);
                        }
                        break;
                    }

                    currentTime += TimeSpan.FromMinutes(vatsimEvent.CalculationParameters.SimulationAnalysisResolutionInMinutes);
                }
            }
            while (!finalSegment);
        }
    }
}
