using CoordinateSharp;
using System.Runtime.InteropServices;

namespace CTPSimulator
{
    public static class Simulator
    {
        public static async Task SimulateEvent(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            // STEP 1: CALCULATE SLOT TIMINGS
            if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode != SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.None)
            {
                // extract slots with unique routings
                Dictionary<Airport, List<List<Slot>>> slotsWithUniqueRoutings = new Dictionary<Airport, List<List<Slot>>>();
                foreach (Slot slot in vatsimEvent.Slots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!slotsWithUniqueRoutings.TryGetValue(slot.DepartureAirport, out var airportSlotSets))
                    {
                        slotsWithUniqueRoutings[slot.DepartureAirport] = [[slot]];
                    }
                    else
                    {
                        var uniqueList = airportSlotSets.Find(ass => ass.First().RouteSegments.SequenceEqual(slot.RouteSegments));
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
                        SimulateSlot(vatsimEvent, DateTimeOffset.MinValue, slotSet.First(), true, cancellationToken);
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

            // STEP 2: SIMULATE SLOTS
            foreach (var slot in vatsimEvent.Slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SimulateSlot(vatsimEvent, slot.DepartureTime, slot, false, cancellationToken);
            }
        }

        private static void SimulateSlot(VATSIMEvent vatsimEvent, DateTimeOffset departureTime, Slot slot, bool synchronizationMode, CancellationToken cancellationToken)
        {
            if (!slot.HasBeenSetupForEnrouteCalculations)
            {
                // check validity of data: are there enough waypoints?
                if (slot.RouteSegments.Count == 0) throw new ArgumentException($"Invalid number of RouteSegments provided for slot {slot.Id} (a minimum of 1 is required).");
                foreach (var routeSegment in slot.RouteSegments) routeSegment.CheckValidity();

                // do all route segments connect to each other?
                for (int r = 0; r < slot.RouteSegments.Count; r++)
                {
                    if (r == 0) // first route segment
                    {
                        if (slot.RouteSegments[r].Locations.First() != slot.DepartureAirport) throw new ArgumentException($"Slot routing for slot {slot.Id} does not start with slot departure airport, but starts with {slot.RouteSegments[r].Locations.First().Identifier} on route {slot.RouteSegments[r].Identifier}.");
                    }
                    else // middle route segment
                    {
                        if (slot.RouteSegments[r - 1].Locations.Last() != slot.RouteSegments[r].Locations.First()) throw new ArgumentException($"Slot routing for slot {slot.Id} has segments that do not connect to each other: {slot.RouteSegments[r - 1].Identifier} not connecting to {slot.RouteSegments[r].Identifier}.");
                    }

                    if (r == slot.RouteSegments.Count - 1) // last route segment
                    {
                        if (slot.RouteSegments[r].Locations.Last() != slot.ArrivalAirport) throw new ArgumentException($"Slot routing for slot {slot.Id} does not end with slot arrival airport, but ends with {slot.RouteSegments[r].Locations.Last().Identifier} on route {slot.RouteSegments[r].Identifier}.");
                    }
                }
            }

            Location origin = slot.RouteSegments.First().Locations.First();

            if (!slot.HasBeenSetupForEnrouteCalculations)
            {
                // concat locations
                slot.RouteWaypoints = [(origin, new Coordinate(origin.Latitude, origin.Longitude, new EagerLoad(false)), slot.RouteSegments.First())];                
                foreach (var segment in slot.RouteSegments)
                {
                    for (int l = 1; l < segment.Locations.Count; l++)
                    {
                        var location = segment.Locations[l];
                        slot.RouteWaypoints.Add((location, new Coordinate(location.Latitude, location.Longitude, new EagerLoad(false)), segment));
                    }
                }

                if (vatsimEvent.CalculationParameters.HighVerbosity)
                {
                    for (int i = 1; i < slot.RouteWaypoints.Count; i++)
                    {
                        var distance = slot.RouteWaypoints[i - 1].Item2.Get_Distance_From_Coordinate(slot.RouteWaypoints[i].Item2, vatsimEvent.CalculationParameters.SimulationEarthShape).NauticalMiles;

                        slot.EnrouteDistances.Add($"{slot.RouteWaypoints[i - 1].Item1.Identifier} ({slot.RouteWaypoints[i - 1].Item2.Latitude.DecimalDegree:F4}, {slot.RouteWaypoints[i - 1].Item2.Longitude.DecimalDegree:F4}) -> " +
                            $"{slot.RouteWaypoints[i].Item1.Identifier} ({slot.RouteWaypoints[i].Item2.Latitude.DecimalDegree:F4}, {slot.RouteWaypoints[i].Item2.Longitude.DecimalDegree:F4}): " +
                            $"{distance:F2} nm");

                        slot.RoutingDistanceInNm += distance;
                    }
                }

                slot.HasBeenSetupForEnrouteCalculations = true;
            }   

            // log takeoff at departure airport
            if (!synchronizationMode)
            {
                int minuteOffset = (int)Math.Round((vatsimEvent.SynchronizationDateTime - departureTime).TotalMinutes);
                LogSlotInThroughputPoint(slot.DepartureAirport, minuteOffset, slot);
            }

            // do the calculations
            DateTimeOffset currentTime = departureTime;
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
                (Location, Coordinate, RouteSegment) nextWaypoint = slot.RouteWaypoints[locationIndex + 1];
                finalSegment = locationIndex == slot.RouteWaypoints.Count - 2;

                var distanceCalculation = currentPosition.Get_Distance_From_Coordinate(nextWaypoint.Item2, vatsimEvent.CalculationParameters.SimulationEarthShape);
                double distanceToNextWaypoint = distanceCalculation.NauticalMiles;
                double bearingToNextWaypoint = distanceCalculation.Bearing;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // calculate speed
                    double groundSpeed = vatsimEvent.CalculationParameters.CalculationFallbackGroundSpeed; // in knots

                    // calculate this time slice
                    double timeSliceDistance = groundSpeed * timeSlice.TotalHours; // in nm

                    if (timeSliceDistance < distanceToNextWaypoint) // waypoint will not be reached within this time slice
                    {
                        distanceToNextWaypoint -= timeSliceDistance;
                        currentTime += timeSlice;
                        currentPosition.Move(nextWaypoint.Item2, bearingToNextWaypoint, vatsimEvent.CalculationParameters.SimulationEarthShape);

                        // we are not in synchronization mode (so log the throughput data)
                        if (!synchronizationMode)
                        {
                            // log this into sectors
                            List<Sector> sectorsToBeChecked;
                            if (vatsimEvent.CalculationParameters.CalculateThroughputDataOnlyForManuallyProvidedSectors)
                            {
                                sectorsToBeChecked = new List<Sector>();
                                foreach (RouteSegment routeSegment in slot.RouteSegments)
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

                            // log tag limits
                            foreach (var tagLimit in nextWaypoint.Item3.TagLimits) LogSlotInThroughputPoint(tagLimit, minuteOffset, slot);

                            // check waypoints
                            if (vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode != SimulatorCalculationParameters.WaypointThroughputCalculationMode.None)
                            {
                                List<Location> waypointsToCheck;
                                if (vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode == SimulatorCalculationParameters.WaypointThroughputCalculationMode.FirstWaypointsOfNATRouteSegmentsOnly)
                                {
                                    waypointsToCheck = vatsimEvent.RouteSegments.Where(rs => rs.Group == "NAT").Select(rs => rs.Locations.First()).ToList();
                                }
                                else if (vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode == SimulatorCalculationParameters.WaypointThroughputCalculationMode.AllWaypoints)
                                {
                                    waypointsToCheck = vatsimEvent.Waypoints;
                                }
                                else throw new NotImplementedException($"WaypointThroughputCalculationMode {vatsimEvent.CalculationParameters.IntendedWaypointThroughputCalculationMode} not implemented.");

                                foreach (var waypoint in waypointsToCheck)
                                {
                                    if (CheckIfPositionIsCloseToAnotherPosition(currentPosition, waypoint.Latitude, waypoint.Longitude,
                                        vatsimEvent.CalculationParameters.SimulationEarthShape, vatsimEvent.CalculationParameters.ThresholdToCheckIfAirplaneIsCountedAtWaypointInNm))
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
                            LogSlotInThroughputPoint(slot.ArrivalAirport, minuteOffset, slot);
                        }

                        break;
                    }
                }
            }
            while (!finalSegment);
        }


        private static void LogSlotInThroughputPoint(ThroughputPoint point, int minuteOffset, Slot slot)
        {
            if (!point.AnalysisFramesViaMinutesFromSynchronizationTimeSlots.TryGetValue(minuteOffset, out List<Slot> value))
            {
                Dictionary<int, List<Slot>> slotsAnalysisFramesViaMinutesFromSynchronizationTime = point.AnalysisFramesViaMinutesFromSynchronizationTimeSlots;
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
            double lat = position.Latitude.DecimalDegree;
            double lon = position.Longitude.DecimalDegree;
            foreach (SectorBoundary sectorBoundary in sector.SectorBoundaries)
            {
                if (sectorBoundary.MaxLatitude >= lat && 
                    sectorBoundary.MinLatitude <= lat && 
                    sectorBoundary.MaxLongitude >= lon && 
                    sectorBoundary.MinLongitude <= lon && 
                    CoordinatesAreInPolygon(lat, lon, sectorBoundary.Coordinates))
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
