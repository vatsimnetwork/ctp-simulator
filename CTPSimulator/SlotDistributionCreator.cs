namespace CTPSimulator
{
    public static class SlotDistributionCreator
    {
        public static async Task CreateSlotDistribution(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            // data integrity checking
            foreach (var routeSegment in vatsimEvent.RouteSegments) routeSegment.CheckValidity();

            // extract all possible route segment paths
            var allFirstSegments = vatsimEvent.RouteSegments.Where(rs => !vatsimEvent.RouteSegments.Any(srs => srs.Locations.Last() == rs.Locations.First()));
            List<List<RouteSegment>> allFirstPaths = new();
            foreach (var segment in allFirstSegments) allFirstPaths.Add([segment]);

            List<List<RouteSegment>> allPossiblePaths = new();
            ExtractConnectingPaths(vatsimEvent, allFirstPaths, allPossiblePaths);

            // data integrity checking: delete paths that don't begin and end with airports
            allPossiblePaths.RemoveAll(p => 
                !vatsimEvent.Airports.Any(a => a == p.First().Locations.First()) ||
                !vatsimEvent.Airports.Any(a => a == p.Last().Locations.Last()));

            List<double> pathsMaximumSlots = new();

            // extract throughput points from paths
            for (int i = 0; i < allPossiblePaths.Count; i++)
            {
                var path = allPossiblePaths[i];

                List<ThroughputPoint> throughputPoints = [path.First().Locations.First()];
                foreach (var segment in path)
                {
                    // locations: waypoints and airports
                    for (int l = 1; l < segment.Locations.Count; l++)
                    {
                        throughputPoints.Add(segment.Locations[l]);
                    }

                    // sectors
                    throughputPoints.AddRange(segment.ProvidedFacilityProgression);

                    // tags
                    throughputPoints.AddRange(segment.TagLimits);
                }

                foreach (var throughputPoint in throughputPoints)
                {
                    // ignore constraints with infinity capacity
                    if (throughputPoint.MaximumSlots < ThroughputPoint.InfinityMarker)
                    {
                        throughputPoint.RouteIndexes.Add(i);
                    }
                }

                var maximumSlots = throughputPoints.Min(tp => tp.MaximumSlots);
                if (maximumSlots == 0) allPossiblePaths.RemoveAt(i--); // exclude path if capacity is zero
                else pathsMaximumSlots.Add(maximumSlots);
            }

            var disconnectedAirports = vatsimEvent.Airports.Where(a =>
                !allPossiblePaths.Any(p => p.First().Locations.First() == a) &&
                !allPossiblePaths.Any(p => p.Last().Locations.Last() == a)).ToList();
            if (disconnectedAirports.Count > 0)
            {
                vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Warning, the following airports do not have any connecting airports: {string.Join(", ", disconnectedAirports)}");
            }

            //    uint slotID = 0;

            //    vatsimEvent.Slots.Add(new Slot()
            //    {
            //        Id = slotID,
            //        DepartureAirport = choice.DepartureAirport,
            //        RouteSegments = new()
            //        {
            //            firstRouteSegment, // airport to nat
            //            choice.RouteSegments, // nat track
            //            thirdRouteSegment // nat to airport
            //        },
            //        ArrivalAirport = choice.ArrivalAirport
            //    });
            //    slotID++;

            //int numbersOfSlotsTarget = Math.Min(vatsimEvent.DepartureAirports.Sum(da => da.MaximumSlots), vatsimEvent.ArrivalAirports.Sum(aa => aa.MaximumSlots));
            //vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Possible slots allocated: {vatsimEvent.Slots.Count} / {numbersOfSlotsTarget} ({numbersOfSlotsTarget - vatsimEvent.Slots.Count()} remaining)");
            //vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Number of city pairs: " + vatsimEvent.Slots.Select(s => $"{s.DepartureAirport.Id}-{s.ArrivalAirport.Id}").Distinct().Count().ToString());

            //List<int> numbersOfRoutingsPerCityPair = new();
            //foreach (var departureAirport in vatsimEvent.DepartureAirports)
            //{
            //    List<string> rowContent = [departureAirport.Identifier];
            //    foreach (var arrivalAirport in vatsimEvent.ArrivalAirports)
            //    {
            //        var slots = vatsimEvent.Slots.FindAll(s => s.DepartureAirport == departureAirport && s.ArrivalAirport == arrivalAirport);
            //        if (slots.Count > 0) numbersOfRoutingsPerCityPair.Add(slots.Select(s => string.Join('-', s.RouteSegments.Select(rs => rs.Id))).Distinct().Count());
            //    }
            //}
            //vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Average number of routings per city pair: {numbersOfRoutingsPerCityPair.Average():F1} (highest: {numbersOfRoutingsPerCityPair.Max()})");
        }


        private static void ExtractConnectingPaths(VATSIMEvent vatsimEvent, IEnumerable<List<RouteSegment>> subPathsUntilNow, List<List<RouteSegment>> allPossiblePaths)
        {
            foreach (var path in subPathsUntilNow) // go through all paths until now
            {
                var connectingSegments = vatsimEvent.RouteSegments.Where(rs =>
                    rs.Locations.First() == path.Last().Locations.Last()).ToList();

                if (connectingSegments.Count == 0) // no more connecting path: add path to allPossiblePaths
                {
                    allPossiblePaths.Add(path);
                }
                else
                {
                    List<List<RouteSegment>> subPaths = new();
                    foreach (var connectingSegment in connectingSegments)
                    {
                        if (path.Contains(connectingSegment)) // segment already exists in path: circular paths are not allowed
                        {
                            throw new ArgumentException($"Error: RouteSegment {connectingSegment.Identifier} is causing a possible circular " +
                                $"route path (segment is connecting to a series of segments of which the segment is already a part of. " +
                                $"Circular route paths are not supported.");
                        }
                        else // create a sub-path
                        {
                            var subPath = new List<RouteSegment>(path)
                            {
                                connectingSegment
                            };
                            subPaths.Add(subPath);
                        }
                    }
                    ExtractConnectingPaths(vatsimEvent, subPaths, allPossiblePaths);
                }
            }
        }
    }
}