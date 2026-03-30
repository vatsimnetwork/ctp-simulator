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
            vatsimEvent.DepartureAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Exists(r => r.LocationsInternal.First() == a)).ToList();
            vatsimEvent.ArrivalAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Exists(r => r.LocationsInternal.Last() == a)).ToList();

            foreach (var airport in vatsimEvent.DepartureAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.LocationsInternal.First() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(
                    r => airport.ConnectingPrimaryRouteSegments.Exists(pr => pr.LocationsInternal.Last() == r.LocationsInternal.First())).ToList();
            }
            foreach (var airport in vatsimEvent.ArrivalAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.LocationsInternal.Last() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(
                    r => airport.ConnectingPrimaryRouteSegments.Exists(tr => tr.LocationsInternal.First() == r.LocationsInternal.Last())).ToList();
            }
            foreach (var airport in vatsimEvent.Airports)
            {
                airport.ConnectingAirports = vatsimEvent.Airports.Where(a => a != airport &&
                airport.ConnectingSecondaryRouteSegments.Intersect(a.ConnectingSecondaryRouteSegments).Any()).ToList();
            }

            if (vatsimEvent.CalculationParameters.RecalculateMaximumAirportSlots) vatsimEvent.ReCalculateMaximumThroughputPointSlots();

            // data integrity checking
            List<string> commentary = new();

            // check for disconnected route segments
            //var disconnectedRouteSegments = vatsimEvent.RouteSegments.Where(rs =>
            //    rs.Enabled &&
            //    (!vatsimEvent.DepartureAirports.Exists(da => 
            //        da.ConnectingPrimaryRouteSegments.Contains(rs) ||
            //        da.ConnectingSecondaryRouteSegments.Contains(rs)) ||
            //    !vatsimEvent.ArrivalAirports.Exists(aa =>
            //         aa.ConnectingPrimaryRouteSegments.Contains(rs) ||
            //        aa.ConnectingSecondaryRouteSegments.Contains(rs))
            //        )).ToList();

            var disconnectedAirports = vatsimEvent.Airports.Where(a => a.ConnectingAirports.Count == 0).Select(a => a.Identifier).ToList();
            if (disconnectedAirports.Count > 0)
            {
                commentary.Add($"Warning, the following airports do not have any connecting airports: {string.Join(", ", disconnectedAirports)}");
            }

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

                var firstRouteSegment = choice.DepartureAirport.ConnectingPrimaryRouteSegments.Where(pr => pr.LocationsInternal.Last() == choice.RouteSegment.LocationsInternal.First()).OrderBy(pr => pr.SlotsAllocated).First();
                firstRouteSegment.SlotsAllocated++;

                var thirdRouteSegment = choice.ArrivalAirport.ConnectingPrimaryRouteSegments.Where(pr => pr.LocationsInternal.First() == choice.RouteSegment.LocationsInternal.Last()).OrderBy(pr => pr.SlotsAllocated).First();
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

            vatsimEvent.CalculationParameters.SlotGenerationOutputCommentary = string.Join(Environment.NewLine + Environment.NewLine, commentary);
        }
    }
}