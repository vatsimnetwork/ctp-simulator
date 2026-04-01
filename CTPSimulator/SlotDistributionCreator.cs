using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Net;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace CTPSimulator
{
    public static class SlotDistributionCreator
    {
        static readonly Random Random = new Random();
        static T PickOne<T>(this List<T> input) => input[Random.Next(input.Count)];

        record SlotChoice
        {
            public Airport DepartureAirport { get; }
            public RouteSegment RouteSegment { get; }
            public Airport ArrivalAirport { get; }
            public int PossiblePaths { get; }
            public int CombinedSlotsAvailable { get; }
            public double DepartureVoteDeficit { get; }
            public double ArrivalVoteDeficit { get; }

            public SlotChoice(Airport departureAirport, RouteSegment routeSegment, Airport arrivalAirport, int possiblePaths, int combinedSlotsAvailable, double departureVoteDeficit, double arrivalVoteDeficit)
            {
                DepartureAirport = departureAirport;
                RouteSegment = routeSegment;
                ArrivalAirport = arrivalAirport;
                PossiblePaths = possiblePaths;
                CombinedSlotsAvailable = combinedSlotsAvailable;
                DepartureVoteDeficit = departureVoteDeficit;
                ArrivalVoteDeficit = arrivalVoteDeficit;
            }
        }

  

        public static async Task CreateSlotDistribution(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            // prepare data
            vatsimEvent.DepartureAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Any(r => r.Locations.First() == a)).ToList();
            vatsimEvent.ArrivalAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Any(r => r.Locations.Last() == a)).ToList();

            foreach (var airport in vatsimEvent.DepartureAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Locations.First() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(
                    r => airport.ConnectingPrimaryRouteSegments.Any(pr => pr.Locations.Last() == r.Locations.First())).ToList();
            }
            foreach (var airport in vatsimEvent.ArrivalAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Locations.Last() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(
                    r => airport.ConnectingPrimaryRouteSegments.Any(tr => tr.Locations.First() == r.Locations.Last())).ToList();
            }
            foreach (var airport in vatsimEvent.Airports)
            {
                airport.ConnectingAirports = vatsimEvent.Airports.Where(a => a != airport &&
                airport.ConnectingSecondaryRouteSegments.Intersect(a.ConnectingSecondaryRouteSegments).Any()).ToList();
            }

            // data integrity checking
            var disconnectedAirports = vatsimEvent.Airports.Where(a => a.ConnectingAirports.Count == 0).Select(a => a.Identifier).ToList();
            if (disconnectedAirports.Count > 0)
            {
                vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Warning, the following airports do not have any connecting airports: {string.Join(", ", disconnectedAirports)}");
            }

            // Pre-calculate total votes for departure airports for proportional allocation
            int totalDepartureVotes = vatsimEvent.DepartureAirports.Sum(da => da.NumberOfVotes);
            int totalArrivalVotes = vatsimEvent.ArrivalAirports.Sum(aa => aa.NumberOfVotes);

            uint slotID = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Compute how many slots have been allocated across all departure airports so far,
                // so we can calculate each airport's proportional vote target and its deficit.
                int totalSlotsAllocated = vatsimEvent.DepartureAirports.Sum(a => a.SlotsAllocated);

                // calculate arrival vote deficit
                Dictionary<Airport, double> arrivalVoteDeficits = new();
                foreach (var arrivalAirport in vatsimEvent.ArrivalAirports)
                {
                    double voteTarget = totalArrivalVotes > 0 ? ((double)arrivalAirport.NumberOfVotes / totalArrivalVotes) * totalSlotsAllocated : 0;
                    double arrivalVoteDeficit = voteTarget - arrivalAirport.SlotsAllocated;
                    arrivalVoteDeficits[arrivalAirport] = arrivalVoteDeficit;
                }

                // take all possible options where slots could go
                List<SlotChoice> choices = new();
                foreach (var departureAirport in vatsimEvent.DepartureAirports.Where(da => da.AreSlotsStillAvailable))
                {
                    // How far behind its vote-proportional target is this airport?
                    // Positive = underserved, negative = overserved.
                    double departureVoteTarget = totalDepartureVotes > 0 ? ((double)departureAirport.NumberOfVotes / totalDepartureVotes) * totalSlotsAllocated : 0;
                    double departureVoteDeficit = departureVoteTarget - departureAirport.SlotsAllocated;              

                    var possibleSecondaryRouteSegments = departureAirport.ConnectingSecondaryRouteSegments
                        .Where(sr =>
                            sr.AreSlotsStillAvailable &&

                            !sr.TagLimits.Any(tl => !tl.AreSlotsStillAvailable) &&
                            !sr.ProvidedFacilityProgression.Any(s => !s.AreSlotsStillAvailable) &&

                            // At least one primary departure feeder with no blocked tags/sectors exists for this oceanic entry point
                            departureAirport.ConnectingPrimaryRouteSegments
                                .Any(pr => pr.Locations.Last() == sr.Locations.First() && 
                                RouteSegmentSectorsAndTagsHaveAvailableSlots(pr)))
                        .ToList();

                    foreach (var routeSegment in possibleSecondaryRouteSegments)
                    {
                        var possibleArrivals = vatsimEvent.ArrivalAirports
                            .Where(aa =>
                                aa.AreSlotsStillAvailable &&
                                aa.ConnectingSecondaryRouteSegments.Contains(routeSegment) &&

                                // At least one primary arrival feeder with no blocked tags/sectors exists for this oceanic exit point
                                aa.ConnectingPrimaryRouteSegments
                                    .Any(pr => pr.Locations.First() == routeSegment.Locations.Last() &&
                                    RouteSegmentSectorsAndTagsHaveAvailableSlots(pr)))
                                .ToList();

                        foreach (var arrivalAirport in possibleArrivals)
                        {
                            choices.Add(new(departureAirport, routeSegment, arrivalAirport,
                                possibleSecondaryRouteSegments.Count + possibleArrivals.Count,
                                departureAirport.SlotsStillAvailable + routeSegment.SlotsStillAvailable + arrivalAirport.SlotsStillAvailable,
                                departureVoteDeficit, arrivalVoteDeficits[arrivalAirport]));
                        }
                    }
                }

                if (choices.Count == 0) break;

                SlotChoice choice;
                if (vatsimEvent.CalculationParameters.IntendedSlotGenerationMode == SimulatorCalculationParameters.SlotGenerationMode.MaximizeAirportPairs)
                {
                    // Vote-proportional algorithm: airports furthest behind their vote-share target get priority,
                    // then fall back to the greedy criteria for tiebreaking within that airport's options.
                    choice = choices
                        .OrderByDescending(c => c.DepartureVoteDeficit + c.ArrivalVoteDeficit)
                        .ThenByDescending(c => c.RouteSegment.GetNumberOfSlotsPerAirportPair(c.DepartureAirport, c.ArrivalAirport))
                        .ThenBy(c => c.PossiblePaths)
                        .ThenByDescending(c => c.CombinedSlotsAvailable)
                        .First();
                }
                else if (vatsimEvent.CalculationParameters.IntendedSlotGenerationMode == SimulatorCalculationParameters.SlotGenerationMode.MaximizeSlots)
                {
                    // Vote-proportional algorithm: airports furthest behind their vote-share target get priority,
                    // then fall back to the greedy criteria for tiebreaking within that airport's options.
                    choice = choices
                        .OrderByDescending(c => c.DepartureVoteDeficit)
                        .ThenByDescending(c => c.RouteSegment.GetNumberOfSlotsPerAirportPair(c.DepartureAirport, c.ArrivalAirport))
                        .ThenBy(c => c.PossiblePaths)
                        .ThenByDescending(c => c.CombinedSlotsAvailable)
                        .First();
                }
                else if (vatsimEvent.CalculationParameters.IntendedSlotGenerationMode == SimulatorCalculationParameters.SlotGenerationMode.Random)
                {
                    choice = choices.PickOne();
                }
                else throw new NotImplementedException($"SlotGenerationMode {vatsimEvent.CalculationParameters.IntendedSlotGenerationMode} not implemented.");

                choice.DepartureAirport.SlotsAllocated++;
                choice.RouteSegment.SlotsAllocated++;
                choice.RouteSegment.AddToNumberOfSlotsPerAirportPair(choice.DepartureAirport, choice.ArrivalAirport);
                choice.ArrivalAirport.SlotsAllocated++;

                // Select best primary feeders that respect their own tag/sector limits.
                var firstRouteSegment = choice.DepartureAirport.ConnectingPrimaryRouteSegments
                    .Where(pr =>
                        pr.Locations.Last() == choice.RouteSegment.Locations.First() &&
                        RouteSegmentSectorsAndTagsHaveAvailableSlots(pr))
                    .OrderByDescending(pr => pr.GetNumberOfSlotsPerAirportPair(choice.DepartureAirport, choice.ArrivalAirport)).First();

                firstRouteSegment.SlotsAllocated++;
                firstRouteSegment.AddToNumberOfSlotsPerAirportPair(choice.DepartureAirport, choice.ArrivalAirport);

                var thirdRouteSegment = choice.ArrivalAirport.ConnectingPrimaryRouteSegments
                    .Where(pr =>
                        pr.Locations.First() == choice.RouteSegment.Locations.Last() &&
                        RouteSegmentSectorsAndTagsHaveAvailableSlots(pr))
                    .OrderByDescending(pr => pr.GetNumberOfSlotsPerAirportPair(choice.DepartureAirport, choice.ArrivalAirport)).First();

                thirdRouteSegment.SlotsAllocated++;
                thirdRouteSegment.AddToNumberOfSlotsPerAirportPair(choice.DepartureAirport, choice.ArrivalAirport);

                // Increment each unique tag limit and sector exactly once per slot, regardless of
                // how many of the three route segments share the same tag/sector object.
                var uniqueTagLimits = choice.RouteSegment.TagLimits.Concat(firstRouteSegment.TagLimits)
                    .Concat(thirdRouteSegment.TagLimits)
                    .Distinct();

                foreach (var tagLimit in uniqueTagLimits) tagLimit.SlotsAllocated++;

                var uniqueSectors = choice.RouteSegment.ProvidedFacilityProgression
                        .Concat(firstRouteSegment.ProvidedFacilityProgression)
                        .Concat(thirdRouteSegment.ProvidedFacilityProgression)
                        .Distinct();

                foreach (var s in uniqueSectors) s.SlotsAllocated++;

                vatsimEvent.Slots.Add(new Slot()
                {
                    Id = slotID,
                    DepartureAirport = choice.DepartureAirport,
                    RouteSegments = new()
                    {
                        firstRouteSegment, // airport to nat
                        choice.RouteSegment, // nat track
                        thirdRouteSegment // nat to airport
                    },
                    ArrivalAirport = choice.ArrivalAirport
                });
                slotID++;
            }

            foreach (var tagLimit in vatsimEvent.TagLimits.Where(tl => tl.SlotsAllocated > tl.MaximumSlots))
                vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Warning, tag limit exceeded: {tagLimit.Identifier} ({tagLimit.SlotsAllocated} / {tagLimit.MaximumSlots} slots)");
            foreach (var sector in vatsimEvent.Sectors.Where(s => s.SlotsAllocated > s.MaximumSlots))
                vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Warning, sector limit exceeded: {sector.Identifier} ({sector.SlotsAllocated} / {sector.MaximumSlots} slots)");

            int numbersOfSlotsTarget = Math.Min(vatsimEvent.DepartureAirports.Sum(da => da.MaximumSlots), vatsimEvent.ArrivalAirports.Sum(aa => aa.MaximumSlots));
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Possible slots allocated: {vatsimEvent.Slots.Count} / {numbersOfSlotsTarget} ({numbersOfSlotsTarget - vatsimEvent.Slots.Count()} remaining)");
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Number of city pairs: " + vatsimEvent.Slots.Select(s => $"{s.DepartureAirport.Id}-{s.ArrivalAirport.Id}").Distinct().Count().ToString());

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
            vatsimEvent.CalculationParameters.SlotGenerationOutputComments.Add($"Average number of routings per city pair: {numbersOfRoutingsPerCityPair.Average():F1} (highest: {numbersOfRoutingsPerCityPair.Max()})");
        }

        static bool RouteSegmentSectorsAndTagsHaveAvailableSlots(RouteSegment r) =>
            !r.TagLimits.Any(tl => !tl.AreSlotsStillAvailable) &&
            !r.ProvidedFacilityProgression.Any(s => !s.AreSlotsStillAvailable);
    }
}