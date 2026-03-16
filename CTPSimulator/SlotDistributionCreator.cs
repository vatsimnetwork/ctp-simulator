using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace CTPSimulator
{
    public static class SlotDistributionCreator
    {
        static readonly Random Random = new Random();
        static T PickOne<T>(this List<T> input) => input[Random.Next(input.Count)];

        public static async Task CreateSlotDistribution(VATSIMEvent vatsimEvent, CancellationToken cancellationToken = default)
        {
            // prepare data
            vatsimEvent.DepartureAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Exists(r => r.Locations.First() == a)).ToList();
            vatsimEvent.ArrivalAirports = vatsimEvent.Airports.Where(a => vatsimEvent.RouteSegments.Exists(r => r.Locations.Last() == a)).ToList();

            foreach (var airport in vatsimEvent.DepartureAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Type != RouteSegmentType.NAT &&
                    r.Locations.First() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Type == RouteSegmentType.NAT &&
                    airport.ConnectingPrimaryRouteSegments.Exists(pr => pr.Locations.Last() == r.Locations.First())).ToList();
            }
            foreach (var airport in vatsimEvent.ArrivalAirports)
            {
                airport.ConnectingPrimaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Type != RouteSegmentType.NAT &&
                    r.Locations.Last() == airport).ToList();

                airport.ConnectingSecondaryRouteSegments = vatsimEvent.RouteSegments.Where(r => r.Type == RouteSegmentType.NAT &&
                    airport.ConnectingPrimaryRouteSegments.Exists(tr => tr.Locations.First() == r.Locations.Last())).ToList();
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // take all possible options where slots could go
                List<(Airport, RouteSegment, Airport, int, int, int)> choices = new();
                foreach (var departureAirport in vatsimEvent.DepartureAirports.Where(da => da.AreSlotsStillAvailable))
                {
                    var possibleRouteSegments = departureAirport.ConnectingSecondaryRouteSegments.Where(sr => sr.AreSlotsStillAvailable).ToList();
                    foreach (var routeSegment in possibleRouteSegments)
                    {
                        var possibleArrivals = vatsimEvent.ArrivalAirports.Where(aa => aa.AreSlotsStillAvailable && aa.ConnectingSecondaryRouteSegments.Contains(routeSegment)).ToList();
                        foreach (var arrivalAirport in possibleArrivals)
                        {
                            choices.Add((departureAirport, routeSegment, arrivalAirport,
                                possibleRouteSegments.Count + possibleArrivals.Count,
                                departureAirport.SlotsStillAvailable + routeSegment.SlotsStillAvailable + arrivalAirport.SlotsStillAvailable,
                                departureAirport.NumberOfVotes + arrivalAirport.NumberOfVotes));
                        }
                    }
                }

                if (choices.Count == 0) break;

                var choice = choices.OrderBy(c => c.Item4).ThenByDescending(c => c.Item5).ThenByDescending(c => c.Item6).First();
                choice.Item1.SlotsAllocated++;
                choice.Item2.SlotsAllocated++;
                choice.Item3.SlotsAllocated++;

                var firstRouteSegment = choice.Item1.ConnectingPrimaryRouteSegments.Where(pr => pr.Locations.Last() == choice.Item2.Locations.First()).OrderBy(pr => pr.SlotsAllocated).First();
                firstRouteSegment.SlotsAllocated++;

                var thirdRouteSegment = choice.Item3.ConnectingPrimaryRouteSegments.Where(pr => pr.Locations.First() == choice.Item2.Locations.Last()).OrderBy(pr => pr.SlotsAllocated).First();
                thirdRouteSegment.SlotsAllocated++;

                vatsimEvent.Slots.Add(new Slot()
                {
                    DepartureAirport = choice.Item1,
                    RouteSegments = new()
                    {
                        firstRouteSegment, // airport to nat
                        choice.Item2, // nat track
                        thirdRouteSegment // nat to airport
                    },
                    ArrivalAirport = choice.Item3
                });
            }
        }
    }
}