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
            // STEP 1: CALCULATE SLOT TIMINGS
            if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode != SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.None)
            {
                // Group slots by departure airport and unique routing
                Dictionary<Airport, Dictionary<Airport, List<List<Slot>>>> departureAirportSlotsWithUniqueRoutings = new();
                foreach (Slot slot in vatsimEvent.Slots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!departureAirportSlotsWithUniqueRoutings.TryGetValue(slot.DepartureAirport, out var departureAirportSlotSets))
                    {
                        departureAirportSlotsWithUniqueRoutings[slot.DepartureAirport] = new() { [slot.ArrivalAirport] = [[slot]] };
                    }
                    else if (!departureAirportSlotSets.TryGetValue(slot.ArrivalAirport, out var uniqueAirportPairRoutings))
                    {
                        departureAirportSlotSets[slot.ArrivalAirport] = [[slot]];
                    }
                    else
                    {
                        var uniqueList = uniqueAirportPairRoutings.Find(uapr => uapr.First().RouteSegments.SequenceEqual(slot.RouteSegments));
                        if (uniqueList == null) uniqueAirportPairRoutings.Add([slot]);
                        else uniqueList.Add(slot);
                    }
                }

                // go through all departure airports
                foreach (var departureAirportSlots in departureAirportSlotsWithUniqueRoutings)
                {
                    // When mode is not CalculateSlotTimingsOnly, simulate unique route combinations to calculate departure WINDOW offsets
                    if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode != SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.CalculateSlotTimingsOnly)
                    {
                        // simulate single unique slot
                        var firstSlotsOfEachUniqueRouting = departureAirportSlots.Value.Values.SelectMany(s => s.Select(ss => ss.First()));
                        foreach (var slot in firstSlotsOfEachUniqueRouting)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SimulateSlot(vatsimEvent, DateTimeOffset.MinValue, slot, true, cancellationToken);
                        }

                        // determine which of the calculated times to use
                        TimeSpan timeUntilSynchronizationLongitude;
                        if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode == SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.EarliestRoutes)
                        {
                            timeUntilSynchronizationLongitude = firstSlotsOfEachUniqueRouting.Min(s => s.TimeUntilSynchronizationLongitudeCrossing);
                        }
                        else if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode == SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.LatestRoutes)
                        {
                            timeUntilSynchronizationLongitude = firstSlotsOfEachUniqueRouting.Max(s => s.TimeUntilSynchronizationLongitudeCrossing);
                        }
                        else timeUntilSynchronizationLongitude = TimeSpan.FromSeconds(firstSlotsOfEachUniqueRouting.Average(s => s.TimeUntilSynchronizationLongitudeCrossing.TotalSeconds));

                        // calculate offset
                        departureAirportSlots.Key.DepartureTimeWindowStart = vatsimEvent.SynchronizationDateTime - timeUntilSynchronizationLongitude;
                    }

                    // Distribute slots across the departure window honoring per-arrival-pair window shiftings.
                    DistributeDepartureAirportSlots(vatsimEvent, departureAirportSlots.Key, departureAirportSlots.Value, cancellationToken);
                }
            }


            // STEP 2: SIMULATE SLOTS
            foreach (var slot in vatsimEvent.Slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SimulateSlot(vatsimEvent, slot.DepartureTime, slot, false, cancellationToken);
            }
        }

        private static void DistributeDepartureAirportSlots(
            VATSIMEvent vatsimEvent,
            Airport departureAirport,
            Dictionary<Airport, List<List<Slot>>> arrivalBuckets,
            CancellationToken cancellationToken)
        {
            // Total slots from this departure airport (across all arrivals / unique routings).
            int N = 0;
            foreach (var uniqueRoutings in arrivalBuckets.Values)
                foreach (var slotList in uniqueRoutings)
                    N += slotList.Count;
            if (N == 0) return;

            DateTimeOffset T0 = departureAirport.DepartureTimeWindowStart;
            TimeSpan W = vatsimEvent.CalculationParameters.DepartureTimeWindowLength;
            double baseIntervalSeconds = W.TotalSeconds / N;
            if (baseIntervalSeconds <= 0)
                throw new InvalidOperationException($"DepartureTimeWindowLength must be positive to distribute slots (departure airport {departureAirport.Identifier}).");

            // Per-arrival allowed grid index range and demand.
            var arrivalInfo = new Dictionary<Airport, (int kMin, int kMax, int demand, List<int> assigned)>();
            vatsimEvent.AirportPairDepartureTimeWindowShiftings.TryGetValue(departureAirport, out var shiftingsForDep);

            int kStart = int.MaxValue;
            int kEnd = int.MinValue;

            foreach (var kvp in arrivalBuckets)
            {
                Airport arrival = kvp.Key;
                int demand = 0;
                foreach (var slotList in kvp.Value) demand += slotList.Count;
                if (demand == 0) continue;

                TimeSpan deltaStart = TimeSpan.Zero;
                TimeSpan deltaEnd = TimeSpan.Zero;
                if (shiftingsForDep != null && shiftingsForDep.TryGetValue(arrival, out var shift))
                {
                    deltaStart = shift.Item1;
                    deltaEnd = shift.Item2;
                }

                // t_k = T0 + (k + 0.5) * baseInterval must lie in [T0 + deltaStart, T0 + W + deltaEnd]
                // => k >= deltaStart/baseInterval - 0.5   and   k <= (W + deltaEnd)/baseInterval - 0.5
                double kMinReal = deltaStart.TotalSeconds / baseIntervalSeconds - 0.5;
                double kMaxReal = (W.TotalSeconds + deltaEnd.TotalSeconds) / baseIntervalSeconds - 0.5;
                int kMin = (int)Math.Ceiling(kMinReal - 1e-9);
                int kMax = (int)Math.Floor(kMaxReal + 1e-9);

                if (kMax < kMin)
                {
                    // Shifted window collapses below one grid step: collapse to the single nearest grid index
                    // and let the widening fallback below find additional positions. Warn the user.
                    int kMid = (int)Math.Round((kMinReal + kMaxReal) / 2.0);
                    vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(
                        $"Warning: airport pair {departureAirport.Identifier}->{arrival.Identifier} has a departure window shift that leaves no room for any slot; {demand} slot(s) will be placed near the shifted window anyway and may violate the per-pair 2x spacing rule.");
                    kMin = kMid;
                    kMax = kMid;
                }

                arrivalInfo[arrival] = (kMin, kMax, demand, new List<int>(demand));
                if (kMin < kStart) kStart = kMin;
                if (kMax > kEnd) kEnd = kMax;
            }

            // Primary sweep: 2-step cooldown between same-arrival slots, deadline-based selection.
            // For each arrival with demand d in its allowed range of size S = kMax - kMin + 1,
            // the ideal indices are kMin + (i + 0.5) * (S / d) for i = 0..d-1 — an even spread across
            // its own window. At each grid index we only place an arrival that is at-or-past its next
            // ideal, preferring the most overdue. This stops high-demand pairs from dense-packing the
            // front of the sweep when they in fact have plenty of room to spread out.
            for (int k = kStart; k <= kEnd; k++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Airport best = null;
                double bestDelta = double.NegativeInfinity;
                foreach (var kvp in arrivalInfo)
                {
                    var info = kvp.Value;
                    int remaining = info.demand - info.assigned.Count;
                    if (remaining <= 0) continue;
                    if (k < info.kMin || k > info.kMax) continue;
                    if (info.assigned.Count > 0 && k - info.assigned[info.assigned.Count - 1] < 2) continue;

                    double span = info.kMax - info.kMin + 1.0;
                    double stride = span / info.demand;
                    double ideal = info.kMin + (info.assigned.Count + 0.5) * stride;
                    double delta = k - ideal;
                    if (delta < 0) continue; // not yet due — leave k idle for a more-pressing arrival or empty
                    if (delta > bestDelta)
                    {
                        bestDelta = delta;
                        best = kvp.Key;
                    }
                }

                if (best != null)
                    arrivalInfo[best].assigned.Add(k);
            }

            // Global occupancy (used across relaxation + widening passes).
            var globalOccupied = new HashSet<int>();
            foreach (var kvp in arrivalInfo)
                foreach (var idx in kvp.Value.assigned)
                    globalOccupied.Add(idx);

            // Relaxation pass: for any arrival still under-filled, allow cooldown = 1 (but still unique grid indices).
            foreach (var arrival in arrivalInfo.Keys.ToList())
            {
                var info = arrivalInfo[arrival];
                if (info.assigned.Count >= info.demand) continue;

                for (int k = info.kMin; k <= info.kMax && info.assigned.Count < info.demand; k++)
                {
                    if (globalOccupied.Contains(k)) continue;
                    info.assigned.Add(k);
                    globalOccupied.Add(k);
                }

                arrivalInfo[arrival] = info;
            }

            // Widening fallback: for any arrival still under-filled, expand outward one grid step at a time
            // from its allowed range until we have enough unique positions. This violates the shifted window
            // and/or the 2x spacing rule, so emit a warning but keep going so slots are never dropped.
            foreach (var arrival in arrivalInfo.Keys.ToList())
            {
                var info = arrivalInfo[arrival];
                if (info.assigned.Count >= info.demand) continue;

                int shortfall = info.demand - info.assigned.Count;
                int lo = info.kMin - 1;
                int hi = info.kMax + 1;
                int safetyBudget = Math.Max(1024, info.demand * 4);
                while (info.assigned.Count < info.demand && safetyBudget-- > 0)
                {
                    if (!globalOccupied.Contains(hi))
                    {
                        info.assigned.Add(hi);
                        globalOccupied.Add(hi);
                        if (info.assigned.Count >= info.demand) break;
                    }
                    if (!globalOccupied.Contains(lo))
                    {
                        info.assigned.Add(lo);
                        globalOccupied.Add(lo);
                    }
                    lo--;
                    hi++;
                }

                arrivalInfo[arrival] = info;

                vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(
                    $"Warning: airport pair {departureAirport.Identifier}->{arrival.Identifier} is overbooked for its shifted window ({info.demand} slot(s) requested, {info.demand - shortfall} fit cleanly); remaining {shortfall} placed outside the requested window and the departure rate limit and/or per-pair 2x spacing rule may be violated.");

                if (info.assigned.Count < info.demand)
                    throw new InvalidOperationException(
                        $"Unable to place all slots for {departureAirport.Identifier}->{arrival.Identifier} even after widening (hit safety budget).");
            }

            // Write back DepartureTime values. Within each arrival bucket, flatten unique-routing lists
            // in existing order and pair 1-to-1 with assigned grid indices sorted ascending.
            foreach (var kvp in arrivalBuckets)
            {
                if (!arrivalInfo.TryGetValue(kvp.Key, out var info)) continue;
                info.assigned.Sort();
                int i = 0;
                foreach (var slotList in kvp.Value)
                {
                    foreach (var slot in slotList)
                    {
                        int k = info.assigned[i++];
                        double offsetSeconds = (k + 0.5) * baseIntervalSeconds;
                        slot.DepartureTime = T0 + TimeSpan.FromSeconds(offsetSeconds);
                    }
                }
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
                for (int r = 0; r < slot.RouteSegments.Count; r++)
                {
                    for (int l = 1; l < slot.RouteSegments[r].Locations.Count; l++)
                    {
                        var location = slot.RouteSegments[r].Locations[l];
                        slot.RouteWaypoints.Add((location, new Coordinate(location.Latitude, location.Longitude, new EagerLoad(false)), slot.RouteSegments[r]));
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
                int minuteOffset = (int)Math.Round((departureTime - vatsimEvent.SynchronizationDateTime).TotalMinutes);
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
                        currentPosition.Move(nextWaypoint.Item2, timeSliceDistance * 1852, vatsimEvent.CalculationParameters.SimulationEarthShape);

                        // we are not in synchronization mode (so log the throughput data)
                        if (!synchronizationMode)
                        {
                            // log position
                            slot.SimulatedPositions.Add(currentTime, [currentPosition.Latitude.DecimalDegree, currentPosition.Longitude.DecimalDegree]);

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
                            int minuteOffset = (int)Math.Round((currentTime - vatsimEvent.SynchronizationDateTime).TotalMinutes);
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
                            int minuteOffset = (int)Math.Round((currentTime - vatsimEvent.SynchronizationDateTime).TotalMinutes);
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
