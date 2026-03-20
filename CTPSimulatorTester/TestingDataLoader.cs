using CTPSimulator;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CTPSimulatorTester
{
    public static class TestingDataLoader
    {
        public static VATSIMEvent Load(string eventPrefix)
        {
            VATSIMEvent vatsimEvent = new() 
            {
                Title = eventPrefix
            };


            Dictionary<string, Location> locations = new();
            Dictionary<string, Airport> airports = new();

            // read airports
            foreach (var line in File.ReadAllLines(Path.Combine("TestingData", $"{eventPrefix} Airports.csv")))
            {
                var splits = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (splits.Length != 3) continue;
                var airport = new Airport
                {
                    Identifier = splits[0],
                    NumberOfVotes = ushort.Parse(splits[2]),
                    MaximumAircraftPerHour = (ushort)Math.Round(double.Parse(splits[1]) / 3),
                };

                airports.Add(airport.Identifier, airport);
                locations.Add(airport.Identifier, airport);

                vatsimEvent.Airports.Add(airport);
            }

            // read route segments
            foreach (var line in File.ReadAllLines(Path.Combine("TestingData", $"{eventPrefix} RouteSegments.csv")))
            {
                var splits = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (splits.Length != 3) continue;
                var routeSegment = new RouteSegment() { Identifier = splits[0], RouteString = splits[1], RouteSegmentGroup = splits[2] };
                foreach (var waypoint in splits[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Char.IsDigit(waypoint.Last()) || waypoint == "DCT") continue; // exclude airways and directs
                    if (!locations.TryGetValue(waypoint, out var location))
                    {
                        location = new Location() { Identifier = waypoint };
                        locations.Add(waypoint, location);
                    }
                    routeSegment.Locations.Add(location);
                    vatsimEvent.Waypoints.Add(location);
                }
                vatsimEvent.RouteSegments.Add(routeSegment);
            }

            return vatsimEvent;
        }
    }
}
