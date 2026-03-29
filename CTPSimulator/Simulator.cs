using CoordinateSharp;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CTPSimulator
{
    public static class Simulator
    {
        public static async Task SimulateEvent(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode != SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.None)
            {
                // extract slots qith unique routings
                Dictionary<Airport, List<List<Slot>>> slotsWithUniqueRoutings = new Dictionary<Airport, List<List<Slot>>>();
                foreach (Slot slot in vatsimEvent.Slots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!slotsWithUniqueRoutings.TryGetValue(slot.DepartureAirportInternal, out var airportSlotSets))
                    {
                        slotsWithUniqueRoutings[slot.DepartureAirportInternal] = [[slot]];
                    }
                    else
                    {
                        var uniqueList = airportSlotSets.Find(ass => ass.First().RouteSegmentsInternal.SequenceEqual(slot.RouteSegmentsInternal));
                        if (uniqueList == null) airportSlotSets.Add([slot]);
                        else uniqueList.Add(slot);
                    }
                    airportSlotSets = null;
                }

                // calculate the departure time offsets
                foreach (var departureSlots in slotsWithUniqueRoutings)
                {
                    // simulate single unique slot
                    foreach (var slotSet in departureSlots.Value)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SimulateSlot(vatsimEvent, DateTime.MinValue, slotSet.First(), true, cancellationToken);
                    }

                    // determine which of the calculated times to use
                    TimeSpan timeUntilSynchronizationLongitude;
                    if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode == SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.EarliestRoutes)
                    {
                        timeUntilSynchronizationLongitude = departureSlots.Value.Min(ds => ds.First().TimeUntilSynchronizationLongitudeCrossing);
                    }
                    else if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode == SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.LatestRoutes)
                    {
                        timeUntilSynchronizationLongitude = departureSlots.Value.Max(ds => ds.First().TimeUntilSynchronizationLongitudeCrossing);
                    }
                    else timeUntilSynchronizationLongitude = TimeSpan.FromSeconds(departureSlots.Value.Average(ds => ds.First().TimeUntilSynchronizationLongitudeCrossing.TotalSeconds));

                    // calculate offset
                    departureSlots.Key.DepartureTimeWindowStart = vatsimEvent.SynchronizationDateTime - timeUntilSynchronizationLongitude;

                    // calculate actual slot data
                    List<(Slot, double)> distributedSlots = new();
                    foreach (var slotSet in departureSlots.Value)
                    {
                        for (int i = 0; i < slotSet.Count; i++)
                        {
                            double ordinator = slotSet.Count == 1 ? 0.5 : (double)i / (slotSet.Count - 1);
                            distributedSlots.Add((slotSet[i], ordinator));
                        }
                    }

                    distributedSlots = distributedSlots.OrderBy(s => s.Item2).ToList();
                    foreach (var slot in distributedSlots)
                    {
                        slot.Item1.DepartureTime = departureSlots.Key.DepartureTimeWindowStart + vatsimEvent.DepartureTimeWindow * slot.Item2;
                    }
                }
            }

            // calculate all slots
            foreach (var slot in vatsimEvent.Slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SimulateSlot(vatsimEvent, slot.DepartureTime, slot, false, cancellationToken);
            }
        }

        private static void SimulateSlot(VATSIMEvent vatsimEvent, DateTime departureTime, Slot slot, bool synchronizationMode, CancellationToken cancellationToken)
        {
            // check validity of data
            if (slot.RouteSegmentsInternal.Count == 0) throw new ArgumentException($"Invalid number of RouteSegments provided for a slot (a minimum of 1 is required).");
            foreach (var routeSegment in slot.RouteSegmentsInternal) routeSegment.CheckValidity();

            Location origin = slot.RouteSegmentsInternal.First().Locations.First();

            // concat locations
            List<(Location, Coordinate, RouteSegment)> locations = [(origin, new Coordinate(origin.Latitude, origin.Longitude, new EagerLoad(false)), slot.RouteSegmentsInternal.First())];
            for (int r = 0; r < slot.RouteSegmentsInternal.Count; r++)
            {
                for (int l = 1; l < slot.RouteSegmentsInternal[r].Locations.Count; l++)
                {
                    var location = slot.RouteSegmentsInternal[r].Locations[l];
                    locations.Add((location, new Coordinate(location.Latitude, location.Longitude, new EagerLoad(false)), slot.RouteSegmentsInternal[r]));
                }
            }

            // calculation precision
            Shape earthShape = vatsimEvent.CalculationParameters.HighSimulationAccuracy ? Shape.Ellipsoid : Shape.Sphere;

            // do the calculations
            DateTime currentTime = departureTime;
            int locationIndex = -1;
            TimeSpan timeSlice = TimeSpan.FromMinutes(vatsimEvent.CalculationParameters.SimulationAnalysisResolutionInMinutes);
            bool finalSegment;
            double previousDifferenceToSynchronizationLongitude = origin.Longitude - vatsimEvent.CalculationParameters.DepartureTimeWindowOffsetSynchronizationLongitude;
            Coordinate currentPosition = new Coordinate(origin.Latitude, origin.Longitude, new EagerLoad(false));

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                // shift waypoint
                locationIndex++;
                (Location, Coordinate, RouteSegment) nextWaypoint = locations[locationIndex + 1];
                finalSegment = locationIndex == locations.Count - 2;

                var distanceCalculation = currentPosition.Get_Distance_From_Coordinate(nextWaypoint.Item2, earthShape);
                double distanceToNextWaypoint = distanceCalculation.NauticalMiles;
                double bearingToNextWaypoint = distanceCalculation.Bearing;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // calculate speed
                    double groundSpeed = vatsimEvent.CalculationParameters.CalculationFallbackGroundSpeed; // in knots

                    // calculate current position
                    double timeSliceDistance = groundSpeed * timeSlice.TotalHours; // in nm
                    if (timeSliceDistance < distanceToNextWaypoint) // waypoint will not be reached within this time slice
                    {
                        distanceToNextWaypoint -= timeSliceDistance;
                        currentTime += timeSlice;
                        currentPosition.Move(nextWaypoint.Item2, new Distance(timeSliceDistance, DistanceType.NauticalMiles), earthShape);

                        // we are not in synchronization mode (so log the throughput data)
                        if (!synchronizationMode)
                        {
                            // log this into sectors
                            List<Sector> sectorsToBeChecked;
                            if (vatsimEvent.CalculationParameters.CalculateThroughputDataOnlyForManuallyProvidedSectors)
                            {
                                sectorsToBeChecked = new List<Sector>();
                                foreach (RouteSegment routeSegment in slot.RouteSegmentsInternal)
                                {
                                    foreach (Sector sector in routeSegment.ProvidedFacilityProgression)
                                    {
                                        if (!sectorsToBeChecked.Contains(sector))
                                        {
                                            sectorsToBeChecked.Add(sector);
                                        }
                                    }
                                }
                            }
                            else // include all sectors
                            {
                                sectorsToBeChecked = vatsimEvent.Sectors;
                            }

                            // log sectors
                            int minuteOffset = (int)Math.Round((vatsimEvent.SynchronizationDateTime - currentTime).TotalMinutes);
                            foreach (Sector sector in sectorsToBeChecked)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (SectorContainsPosition(currentPosition, sector))
                                {
                                    LogSlotInThroughputPoint(sector, minuteOffset, slot);
                                }
                            }

                            // log route element
                            LogSlotInThroughputPoint(nextWaypoint.Item3, minuteOffset, slot);

                            // check waypoints
                            if (vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode != SimulatorCalculationParameters.WaypointThroughputCalculationMode.None)
                            {
                                List<Location> waypointsToCheck;
                                if (vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode == SimulatorCalculationParameters.WaypointThroughputCalculationMode.FirstWaypointsOfNATRouteSegmentsOnly)
                                {
                                    waypointsToCheck = vatsimEvent.RouteSegments.Where(rs => rs.RouteSegmentGroup == "NAT").Select(rs => rs.Locations.First()).ToList();
                                }
                                else if (vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode == SimulatorCalculationParameters.WaypointThroughputCalculationMode.AllWaypoints)
                                {
                                    waypointsToCheck = vatsimEvent.Waypoints;
                                }
                                else throw new NotImplementedException($"WaypointThroughputCalculationMode {vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode} not implemented.");

                                foreach (var waypoint in waypointsToCheck)
                                {
                                    if (CheckIfPositionIsCloseToAnotherPosition(currentPosition, waypoint.Latitude, waypoint.Longitude, 
                                        earthShape, vatsimEvent.CalculationParameters.ThresholdToCheckIfAirplaneIsCountedAtWaypointInNm))
                                    {
                                        LogSlotInThroughputPoint(waypoint, minuteOffset, slot);
                                    }
                                }
                            }
                        }
                        else // check sync longitude
                        {
                            double differenceToSynchronizationLongitude = currentPosition.Longitude.DecimalDegree - vatsimEvent.CalculationParameters.DepartureTimeWindowOffsetSynchronizationLongitude;
                            if (Math.Sign(previousDifferenceToSynchronizationLongitude) != Math.Sign(differenceToSynchronizationLongitude))
                            {
                                slot.TimeUntilSynchronizationLongitudeCrossing = currentTime - departureTime;
                                return;
                            }
                            previousDifferenceToSynchronizationLongitude = differenceToSynchronizationLongitude;
                        }

                        timeSlice = TimeSpan.FromMinutes(vatsimEvent.CalculationParameters.SimulationAnalysisResolutionInMinutes);
                    }
                    else // we reach the next waypoint within this time slice
                    {
                        TimeSpan timeUntilNextWaypoint = TimeSpan.FromHours(distanceToNextWaypoint / groundSpeed);
                        currentTime += timeUntilNextWaypoint;
                        currentPosition = nextWaypoint.Item2;  

                        if (!finalSegment)
                        {
                            timeSlice -= timeUntilNextWaypoint;
                        }
                        else if (!synchronizationMode) // final segment: log arrival time
                        {
                            slot.ProjectedArrivalTime = currentTime;
                            int minuteOffset = (int)Math.Round((vatsimEvent.SynchronizationDateTime - currentTime).TotalMinutes);
                            LogSlotInThroughputPoint(slot.ArrivalAirportInternal, minuteOffset, slot);
                        }

                        break;
                    }
                }
            }
            while (!finalSegment);
        }


        private static void LogSlotInThroughputPoint(ThroughputPoint point, int minuteOffset, Slot slot)
        {
            if (!point.SlotsAnalysisFramesViaMinutesFromSynchronizationTimeInternal.TryGetValue(minuteOffset, out List<Slot> value))
            {
                Dictionary<int, List<Slot>> slotsAnalysisFramesViaMinutesFromSynchronizationTime = point.SlotsAnalysisFramesViaMinutesFromSynchronizationTimeInternal;
                int num = 1;
                List<Slot> list = new List<Slot>(num);
                CollectionsMarshal.SetCount(list, num);
                CollectionsMarshal.AsSpan(list)[0] = slot;
                slotsAnalysisFramesViaMinutesFromSynchronizationTime[minuteOffset] = list;
            }
            else
            {
                value.Add(slot);
            }
        }


        private static bool SectorContainsPosition(Coordinate position, Sector sector)
        {
            double decimalDegree = position.Latitude.DecimalDegree;
            double decimalDegree2 = position.Longitude.DecimalDegree;
            foreach (SectorBoundary sectorBoundary in sector.SectorBoundaries)
            {
                if (sectorBoundary.MaxLatitude > decimalDegree && 
                    sectorBoundary.MinLatitude < decimalDegree && 
                    sectorBoundary.MaxLongitude > decimalDegree2 && 
                    sectorBoundary.MinLongitude < decimalDegree2 && 
                    CoordinatesAreInPolygon(decimalDegree, decimalDegree2, sectorBoundary.Coordinates))
                {
                    return true;
                }
            }
            return false;
        }


        const double SmallestLatitudeDegreeDistanceInNm = 59.701404;
        private static bool CheckIfPositionIsCloseToAnotherPosition(Coordinate coordinate1, double coordinate2Lat, double coordinate2Lon, Shape earthShape, double thresholdInNm)
        {
            if (Math.Abs(coordinate1.Latitude.DecimalDegree - coordinate2Lat) * SmallestLatitudeDegreeDistanceInNm >=
                thresholdInNm) return false;
            return coordinate1.Get_Distance_From_Coordinate(new Coordinate(coordinate2Lat, coordinate2Lon, new EagerLoad(false)), earthShape).NauticalMiles >= thresholdInNm;
        }

        private static bool CoordinatesAreInPolygon(double lat, double lon, double[,] polygonCoordinates)
        {
            // https://en.wikipedia.org/wiki/Point_in_polygon
            int i, j;
            bool c = false;
            for (i = 0, j = polygonCoordinates.GetLength(0) - 1; i < polygonCoordinates.GetLength(0); j = i++)
            {
                if ((((polygonCoordinates[i, 0] <= lat) && (lat < polygonCoordinates[j, 0]))
                        || ((polygonCoordinates[j, 0] <= lat) && (lat < polygonCoordinates[i, 0])))
                        && (lon < (polygonCoordinates[j, 1] - polygonCoordinates[i, 1]) * (lat - polygonCoordinates[i, 0])
                            / (polygonCoordinates[j, 0] - polygonCoordinates[i, 0]) + polygonCoordinates[i, 1]))

                    c = !c;
            }
            return c;
        }
    }
}
