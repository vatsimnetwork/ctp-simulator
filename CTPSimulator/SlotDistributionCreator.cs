using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Net;
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
            public int CombinedVotes { get; }

            public SlotChoice(Airport departureAirport, RouteSegment routeSegment, Airport arrivalAirport, int possiblePaths, int combinedSlotsAvailable, int combinedVotes)
            {
                DepartureAirport = departureAirport;
                RouteSegment = routeSegment;
                ArrivalAirport = arrivalAirport;
                PossiblePaths = possiblePaths;
                CombinedSlotsAvailable = combinedSlotsAvailable;
                CombinedVotes = combinedVotes;
            }
        }

        public static async Task CreateSlotDistribution(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            // prepare data
            vatsimEvent.DepartureAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Exists(r => r.Locations.First() == a)).ToList();
            vatsimEvent.ArrivalAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Exists(r => r.Locations.Last() == a)).ToList();

            foreach (var airport in vatsimEvent.DepartureAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Locations.First() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(
                    r => airport.ConnectingPrimaryRouteSegments.Exists(pr => pr.Locations.Last() == r.Locations.First())).ToList();
            }
            foreach (var airport in vatsimEvent.ArrivalAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Locations.Last() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(
                    r => airport.ConnectingPrimaryRouteSegments.Exists(tr => tr.Locations.First() == r.Locations.Last())).ToList();
            }

            if (vatsimEvent.CalculationParameters.RecalculateMaximumAirportSlots) vatsimEvent.ReCalculateMaximumThroughputPointSlots();

            uint slotID = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // take all possible options where slots could go
                List<SlotChoice> choices = new();
                foreach (var departureAirport in vatsimEvent.DepartureAirports.Where(da => da.AreSlotsStillAvailable))
                {
                    var possibleRouteSegments = departureAirport.ConnectingSecondaryRouteSegments.Where(sr => sr.AreSlotsStillAvailable).ToList();
                    foreach (var routeSegment in possibleRouteSegments)
                    {
                        var possibleArrivals = vatsimEvent.ArrivalAirports.Where(aa => aa.AreSlotsStillAvailable && aa.ConnectingSecondaryRouteSegments.Contains(routeSegment)).ToList();
                        foreach (var arrivalAirport in possibleArrivals)
                        {
                            choices.Add(new(departureAirport, routeSegment, arrivalAirport,
                                possibleRouteSegments.Count + possibleArrivals.Count,
                                departureAirport.SlotsStillAvailable + routeSegment.SlotsStillAvailable + arrivalAirport.SlotsStillAvailable,
                                departureAirport.NumberOfVotes + arrivalAirport.NumberOfVotes));
                        }
                    }
                }

                if (choices.Count == 0) break;

                SlotChoice choice;
                if (vatsimEvent.CalculationParameters.IntendedSlotGenerationMode == SimulatorCalculationParameters.SlotGenerationMode.MaximizeSlots)
                {
                    choice = choices.OrderBy(c => c.PossiblePaths).ThenByDescending(c => c.CombinedSlotsAvailable).ThenByDescending(c => c.CombinedVotes).First();
                }
                else if (vatsimEvent.CalculationParameters.IntendedSlotGenerationMode == SimulatorCalculationParameters.SlotGenerationMode.Random)
                {
                    choice = choices.PickOne();
                }
                else throw new NotImplementedException($"SlotGenerationMode {vatsimEvent.CalculationParameters.IntendedSlotGenerationMode} not implemented.");

                choice.DepartureAirport.SlotsAllocated++;
                choice.RouteSegment.SlotsAllocated++;
                choice.ArrivalAirport.SlotsAllocated++;

                var firstRouteSegment = choice.DepartureAirport.ConnectingPrimaryRouteSegments.Where(pr => pr.Locations.Last() == choice.RouteSegment.Locations.First()).OrderBy(pr => pr.SlotsAllocated).First();
                firstRouteSegment.SlotsAllocated++;

                var thirdRouteSegment = choice.ArrivalAirport.ConnectingPrimaryRouteSegments.Where(pr => pr.Locations.First() == choice.RouteSegment.Locations.Last()).OrderBy(pr => pr.SlotsAllocated).First();
                thirdRouteSegment.SlotsAllocated++;

                vatsimEvent.Slots.Add(new Slot()
                {
                    Id = slotID,
                    DepartureAirportInternal = choice.DepartureAirport,
                    RouteSegmentsInternal = new()
                    {
                        firstRouteSegment, // airport to nat
                        choice.RouteSegment, // nat track
                        thirdRouteSegment // nat to airport
                    },
                    ArrivalAirportInternal = choice.ArrivalAirport
                });
                slotID++;
            }
        }
    }
}