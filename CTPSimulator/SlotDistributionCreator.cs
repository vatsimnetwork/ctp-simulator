using System.Runtime.InteropServices;

namespace CTPSimulator
{
    public static class SlotDistributionCreator
    {
        public static async Task CreateSlotDistribution(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            // data integrity checking
            foreach (var routeSegment in vatsimEvent.RouteSegments) routeSegment.CheckValidity();

            // extract all possible route segment paths
            var allFirstSegments = vatsimEvent.RouteSegments.Where(rs => rs.Enabled && !vatsimEvent.RouteSegments.Any(srs => srs.Locations.Last() == rs.Locations.First()));
            List<List<RouteSegment>> allFirstPaths = new();
            foreach (var segment in allFirstSegments) allFirstPaths.Add([segment]);

            List<List<RouteSegment>> allPossiblePaths = new();
            ExtractConnectingPaths(vatsimEvent, allFirstPaths, allPossiblePaths);

            // data integrity checking: delete paths that don't begin and end with airports
            allPossiblePaths.RemoveAll(p => 
                !vatsimEvent.Airports.Any(a => a == p.First().Locations.First()) ||
                !vatsimEvent.Airports.Any(a => a == p.Last().Locations.Last()));

            // departure and arrival airports
            vatsimEvent.DepartureAirports =
                vatsimEvent.Airports.Where(a => allPossiblePaths.Any(p => p.First().Locations.First() == a)).ToList();
            vatsimEvent.ArrivalAirports =
                vatsimEvent.Airports.Where(a => allPossiblePaths.Any(p => p.Last().Locations.Last() == a)).ToList();

            // check for disconnected airports
            var disconnectedAirports = vatsimEvent.Airports.Where(a => !vatsimEvent.DepartureAirports.Contains(a) && !vatsimEvent.ArrivalAirports.Contains(a)).ToList();
            if (disconnectedAirports.Count > 0)
            {
                vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Warning, the following airports do not have any connecting airports: {string.Join(", ", disconnectedAirports)}");
            }

            double maximumPossibleSlots = Math.Min(vatsimEvent.DepartureAirports.Sum(da => da.MaximumSlots),
                vatsimEvent.ArrivalAirports.Sum(aa => aa.MaximumSlots));
            List<double> pathsMaximumSlots = new();

            // extract throughput point indexes from paths
            Dictionary<ThroughputPoint, HashSet<int>> allThroughputPointIndexes = new();
            var pathThroughputPoints = new List<HashSet<ThroughputPoint>>();

            for (int i = 0; i < allPossiblePaths.Count; i++)
            {
                var path = allPossiblePaths[i];

                HashSet<ThroughputPoint> throughputPoints = [path.First().Locations.First()];
                foreach (var segment in path)
                {
                    // locations: waypoints and airports
                    for (int l = 1; l < segment.Locations.Count; l++)
                    {
                        throughputPoints.Add(segment.Locations[l]);
                    }

                    // sectors
                    foreach (var progression in segment.ProvidedFacilityProgression) throughputPoints.Add(progression);

                    // tags
                    foreach (var tagLimit in segment.TagLimits) throughputPoints.Add(tagLimit);

                    // the routeSegments themselves
                    throughputPoints.Add(segment);
                }

                var maximumSlots = throughputPoints.Min(tp => tp.MaximumSlots);
                if (maximumSlots == 0)
                {
                    allPossiblePaths.RemoveAt(i--); // exclude path completely if capacity is zero
                    continue;
                }
                else if (maximumSlots >= ThroughputPoint.InfinityMarker) pathsMaximumSlots.Add(maximumPossibleSlots);
                else pathsMaximumSlots.Add(maximumSlots);

                // add to throguhput point indexes
                foreach (var throughputPoint in throughputPoints)
                {
                    // ignore constraints with infinity capacity
                    if (throughputPoint.MaximumSlots < ThroughputPoint.InfinityMarker)
                    {
                        if (!allThroughputPointIndexes.TryGetValue(throughputPoint, out var indexes))
                        {
                            indexes = new();
                            allThroughputPointIndexes[throughputPoint] = indexes;
                        }
                        indexes.Add(i);
                    }
                }
                pathThroughputPoints.Add(throughputPoints);
            }

            // extract possible unique city pairs
            Dictionary<(Airport, Airport), int> cityPairSlots = new();
            foreach (var path in allPossiblePaths)
            {
                var departureAirport = (Airport)path.First().Locations.First();
                var arrivalAirport = (Airport)path.Last().Locations.Last();
                var tuple = (departureAirport, arrivalAirport);
                if (!cityPairSlots.ContainsKey(tuple)) cityPairSlots.Add(tuple, 0);
            }

            // create solver
            int numberOfPossiblePaths = allPossiblePaths.Count;
            alglib.minlpsolverstate solver;
            alglib.minlpsolvercreate(new double[numberOfPossiblePaths], out solver);

            // add activation variables (integers) for every pair
            //var relevantVariables = new bool[numberOfPossiblePaths * 2];
            //var integerUpperBounds = new double[numberOfPossiblePaths];
            //for (int i = 0; i < numberOfPossiblePaths; i++)
            //{
            //    integerUpperBounds[i] = 1;
            //    alglib.minlpsolversetintkth(solver, i + numberOfPossiblePaths);
            //}
            //for (int i = 0; i < numberOfPossiblePaths * 2; i++)
            //{
            //    relevantVariables[i] = true;
            //}

            // add scales
            List<double> scales = new();
            double averageMaximumPathSlots = pathsMaximumSlots.Average();
            for (int i = 0; i < numberOfPossiblePaths; i++) scales.Add(averageMaximumPathSlots);
            //for (int i = 0; i < numberOfPossiblePaths; i++) scales.Add(1); // integer scales

            // set bounds & scales
            alglib.minlpsolversetbc(solver, new double[numberOfPossiblePaths * 2], pathsMaximumSlots.ToArray());
            alglib.minlpsolversetscale(solver, scales.ToArray());
            //alglib.minlpsolversetobjectivemaskdense(solver, relevantVariables);

            // set optimizer constraints
            double[] indexArray;

            // set the maximum slots constraint
            indexArray = new double[numberOfPossiblePaths];
            for (int i = 0; i < numberOfPossiblePaths; i++) indexArray[i] = 1;
            alglib.minlpsolveraddlc2dense(solver, indexArray, 0, maximumPossibleSlots);

            // set all the throughput points constraints
            foreach (var throughputPointIndexes in allThroughputPointIndexes)
            {
                indexArray = new double[numberOfPossiblePaths];
                foreach (var index in throughputPointIndexes.Value) indexArray[index] = 1;
                alglib.minlpsolveraddlc2dense(solver, indexArray, 0, throughputPointIndexes.Key.MaximumSlots);
            }

            // set all the big M constriants (couple the integer variables to their slot count variables)
            //for (int i = 0; i < numberOfPossiblePaths; i++)
            //{
            //    indexArray = new double[numberOfPossiblePaths * 2];
            //    indexArray[i] = -1;
            //    indexArray[numberOfPossiblePaths + i] = maximumPossibleSlots;
            //    alglib.minlpsolveraddlc2dense(solver, indexArray, 0, maximumPossibleSlots);
            //}

            // run the optimizer
            OptimizerPayload payload = new(maximumPossibleSlots);
            int batchsize = 1;
            int budget = 0;
            int maxneighborhood = 2000;
            alglib.minlpsolversettimeout(solver, 60);
            alglib.minlpsolversetalgomivns(solver, budget, maxneighborhood, batchsize);

            double[] xf;
            alglib.minlpsolverreport rep;
            var parameters = alglib.parallel | alglib.parallel_callbacks;
            alglib.minlpsolveroptimize(solver, optimize, null, payload, parameters);
            alglib.minlpsolverresults(solver, out xf, out rep);


            // construct the actual slots
            List<int> pathSlotNumbers = new();
            uint slotID = 0;
            for (int p = 0; p < numberOfPossiblePaths; p++)
            {
                int numberOfPathSlots = (int)Math.Round(xf[p]);
                foreach (var throughputPoint in pathThroughputPoints[p]) throughputPoint.SlotsAllocated += numberOfPathSlots;
                var path = allPossiblePaths[p];

                var departureAirport = (Airport)path.First().Locations.First();
                var arrivalAirport = (Airport)path.Last().Locations.Last();
                cityPairSlots[(departureAirport, arrivalAirport)] += numberOfPathSlots;
                pathSlotNumbers.Add(numberOfPathSlots);

                for (int s = 0; s < numberOfPathSlots; s++)
                {
                    vatsimEvent.Slots.Add(new Slot()
                    {
                        Id = slotID,
                        DepartureAirport = departureAirport,
                        RouteSegments = path,
                        ArrivalAirport = arrivalAirport
                    });
                    slotID++;
                }
            }

            // create warnings
            if (CreateThroughputPointExceedanceWarning(vatsimEvent.Airports.Cast<ThroughputPoint>().ToList(), "airports", out string warning)) vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(warning);
            if (CreateThroughputPointExceedanceWarning(vatsimEvent.Waypoints.Cast<ThroughputPoint>().ToList(), "waypoints", out warning)) vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(warning);
            if (CreateThroughputPointExceedanceWarning(vatsimEvent.RouteSegments.Cast<ThroughputPoint>().ToList(), "route segments", out warning)) vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(warning);
            if (CreateThroughputPointExceedanceWarning(vatsimEvent.Sectors.Cast<ThroughputPoint>().ToList(), "sectors", out warning)) vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(warning);
            if (CreateThroughputPointExceedanceWarning(vatsimEvent.TagLimits.Cast<ThroughputPoint>().ToList(), "tag limits", out warning)) vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add(warning);

            // evaluate slot set
            double slotScore = vatsimEvent.Slots.Count / maximumPossibleSlots;
            int numberOfAllocatedCityPairs = cityPairSlots.Count(cp => cp.Value > 0);
            double cityPairScore = (double)numberOfAllocatedCityPairs / cityPairSlots.Count;
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Possible slots allocated: {vatsimEvent.Slots.Count} / {maximumPossibleSlots} ({maximumPossibleSlots - vatsimEvent.Slots.Count()} remaining) | Score: {slotScore:P0}");
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Number of city pairs: {numberOfAllocatedCityPairs} / {cityPairSlots.Count} | Score: {cityPairScore:P0}");

            List<int> numbersOfRoutingsPerCityPair = new();
            foreach (var departureAirport in vatsimEvent.DepartureAirports)
            {
                List<string> rowContent = [departureAirport.Identifier];
                foreach (var arrivalAirport in vatsimEvent.ArrivalAirports)
                {
                    var slots = vatsimEvent.Slots.FindAll(s => s.DepartureAirport == departureAirport && s.ArrivalAirport == arrivalAirport);
                    if (slots.Count > 0) numbersOfRoutingsPerCityPair.Add(slots.Select(s => string.Join('-', s.RouteSegments.Select(rs => rs.Id))).Distinct().Count());
                }
            }

            int totalNumberOfRoutes = pathSlotNumbers.Count(p => p > 0);
            double routeScore = (double)(totalNumberOfRoutes - numberOfAllocatedCityPairs) / (numberOfPossiblePaths - numberOfAllocatedCityPairs);
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Total number of routings: {totalNumberOfRoutes} | Score: {routeScore:P0}");
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Average number of routings per city pair: {numbersOfRoutingsPerCityPair.Average():F1} (highest: {numbersOfRoutingsPerCityPair.Max()})");

            // calculate score
            double totalScore = (slotScore + cityPairScore + routeScore) / 3;
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Total score: {totalScore:P0}");
        }


        private static bool CreateThroughputPointExceedanceWarning(List<ThroughputPoint> throughputPoints, string throughputPointType, out string warning)
        {
            var exceededPoints = throughputPoints.Where(tp => tp.SlotsAllocated > tp.MaximumSlots).ToList();
            if (exceededPoints.Count > 0)
            {
                List<string> tpInfo = new();
                foreach (var point in exceededPoints)
                {
                    tpInfo.Add($"{point.Identifier} ({point.SlotsAllocated} / {point.MaximumSlots})");
                }
                warning = $"Warning: The following {throughputPointType} have exceeded their maximum slot values {string.Join(", ", tpInfo)}";
                return true;
            }
            else
            {
                warning = string.Empty;
                return false;
            }
        }

        private static void ExtractConnectingPaths(VATSIMEvent vatsimEvent, IEnumerable<List<RouteSegment>> subPathsUntilNow, List<List<RouteSegment>> allPossiblePaths)
        {
            foreach (var path in subPathsUntilNow) // go through all paths until now
            {
                var connectingSegments = vatsimEvent.RouteSegments.Where(rs => rs.Enabled &&
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


        // optimization
        record OptimizerPayload(double maximumPossibleSlots)
        {
            public double MaximumPossibleSlots { get; } = maximumPossibleSlots;
        }
        class Scorer
        {
            private double Score;
            private double WeightSum;

            /// <param name="score">From 0 to 1</param>
            /// <param name="weight">Can be any number</param>
            /// <param name="inverted">Should it count as punishment?</param>
            public void AddPartScore(double score, double weight, bool inverted)
            {
                if (inverted) score = 1 - score;
                Score += score * weight;
                WeightSum += weight;
            }

            public double GetScore() => Score / WeightSum;
        }
        public static void optimize(double[] x, double[] fi, object obj)
        {
            var payload = (OptimizerPayload)obj;
            int numberOfPossiblePaths = x.Length / 2;
            Scorer scorer = new();

            // slot sum
            double slotSum = 0;
            for (int i = 0; i < numberOfPossiblePaths; i++) slotSum += x[i];
            double slotScore = slotSum / payload.maximumPossibleSlots; // from 0 to 1
            scorer.AddPartScore(slotScore, 1, false);

            // routes sum
            //double routesSum = 0;
            //for (int i = numberOfPossiblePaths; i < x.Length; i++) routesSum += x[i];
            //double routesScore = routesSum / numberOfPossiblePaths;
            //scorer.AddPartScore(routesScore, 0.01, true);

            fi[0] = 1 - scorer.GetScore();
        }
    }
}