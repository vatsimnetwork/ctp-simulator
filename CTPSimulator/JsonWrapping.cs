using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CTPSimulator
{
    public static class JsonWrapping
    {
        public enum Action { CreateSlotDistribution, SimulateEvent }
        public static JsonSerializerSettings SerializationSettings(Action action, bool highVerbosity) => new() { ContractResolver = new PropertiesResolver(action, highVerbosity) };
        public class JsonIgnoreOnSerializationAttribute : Attribute { }
        public class JsonOnlyOnSimulateEventSerializationAttribute : Attribute { }
        public class JsonOnlyOnCreateSlotDistributionSerializationAttribute : Attribute { }
        public class JsonOnlyOnHighVerbositySerializationAttribute : Attribute { }

        class PropertiesResolver : DefaultContractResolver
        {
            Action Action { get; }
            bool HighVerbosity { get; }
            public PropertiesResolver(Action action, bool highVerbosity)
            {
                Action = action;
                HighVerbosity = highVerbosity;
            }

            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var properties = objectType.GetProperties().Where(p => !Attribute.IsDefined(p, typeof(JsonIgnoreOnSerializationAttribute)));
                if (!HighVerbosity) properties = properties.Where(p => !Attribute.IsDefined(p, typeof(JsonOnlyOnHighVerbositySerializationAttribute)));

                if (Action != Action.CreateSlotDistribution) properties = properties.Where(p => !Attribute.IsDefined(p, typeof(JsonOnlyOnCreateSlotDistributionSerializationAttribute)));
                if (Action != Action.SimulateEvent) properties = properties.Where(p => !Attribute.IsDefined(p, typeof(JsonOnlyOnSimulateEventSerializationAttribute)));

                return properties.ToList<MemberInfo>();
            }
        }


        // wrapping and unwrapping
        public static void UnwrapAllRouteSegmentData(VATSIMEvent vatsimEvent)
        {
            foreach (var airport in vatsimEvent.Airports)
            {
                if (vatsimEvent.AirportsById.ContainsKey(airport.Id) || vatsimEvent.WaypointsById.ContainsKey(airport.Id)) throw new ArgumentException("Duplicate airport / waypoint Id defined: " + airport.Id);
                vatsimEvent.AirportsById[airport.Id] = airport;
            }
            foreach (var waypoint in vatsimEvent.Waypoints)
            {
                if (vatsimEvent.AirportsById.ContainsKey(waypoint.Id) || vatsimEvent.WaypointsById.ContainsKey(waypoint.Id)) throw new ArgumentException("Duplicate airport / waypoint Id defined: " + waypoint.Id);
                vatsimEvent.WaypointsById[waypoint.Id] = waypoint;
            }
            foreach (var sector in vatsimEvent.Sectors)
            {
                if (vatsimEvent.SectorsById.ContainsKey(sector.Id)) throw new ArgumentException("Duplicate sector Id defined: " + sector.Id);
                vatsimEvent.SectorsById[sector.Id] = sector;
            }
            foreach (var tagLimit in vatsimEvent.TagLimits)
            {
                if (vatsimEvent.TagLimitsById.ContainsKey(tagLimit.Id)) throw new ArgumentException("Duplicate TagLimit Id defined: " + tagLimit.Id);
                vatsimEvent.TagLimitsById[tagLimit.Id] = tagLimit;
            }

            // unwrap ids
            foreach (var routeSegment in vatsimEvent.RouteSegments)
            {
                foreach (var locationId in routeSegment.LocationIds)
                {
                    if (vatsimEvent.AirportsById.TryGetValue(locationId, out var airport)) routeSegment.Locations.Add(airport);
                    else if (vatsimEvent.WaypointsById.TryGetValue(locationId, out var waypoint)) routeSegment.Locations.Add(waypoint);
                    else throw new ArgumentException("RouteSegment location Id not defined: " + locationId);
                }

                foreach (var sectorId in routeSegment.ProvidedFacilityProgressionIds)
                {
                    if (vatsimEvent.SectorsById.TryGetValue(sectorId, out var sector)) routeSegment.ProvidedFacilityProgression.Add(sector);
                    else throw new ArgumentException("RouteSegment ProvidedFacilityProgression sector Id not defined: " + sectorId);
                }

                foreach (var tagId in routeSegment.TagLimitIds)
                {
                    if (vatsimEvent.TagLimitsById.TryGetValue(tagId, out var tagLimit)) routeSegment.TagLimits.Add(tagLimit);
                    else throw new ArgumentException("RouteSegment TagLimit Id not defined: " + tagId);
                }
            }
        }

        public static void UnwrapSectorBoundaries(VATSIMEvent vatsimEvent, Dictionary<string, List<SectorBoundary>> sectorBoundaries)
        {
            List<string> sectorsWithoutBoundaries = new();
            foreach (var sector in vatsimEvent.Sectors)
            {
                if (sectorBoundaries.TryGetValue(sector.Identifier, out var boundaries))
                {
                    sector.SectorBoundaries = boundaries;
                }
                else
                {
                    sectorsWithoutBoundaries.Add(sector.Identifier);
                }
            }
            if (sectorsWithoutBoundaries.Count > 0)
            {
                vatsimEvent.CalculationParameters.SimulationOutputComments.Add(
                    $"Warning, the following sectors do not have any sector boundaries defined, " +
                    $"therefore no throughput analysis can be calculated: {string.Join(", ", sectorsWithoutBoundaries)}");
            }
        }

        public static void UnwrapAllSlotAirportsAndRouteSegments(VATSIMEvent vatsimEvent)
        {
            foreach (var routeSegment in vatsimEvent.RouteSegments)
            {
                if (vatsimEvent.RouteSegmentsById.ContainsKey(routeSegment.Id)) throw new ArgumentException("Duplicate RouteSegment Id defined: " + routeSegment.Id);
                vatsimEvent.RouteSegmentsById[routeSegment.Id] = routeSegment;
            }

            foreach (var slot in vatsimEvent.Slots)
            {
                if (vatsimEvent.AirportsById.TryGetValue(slot.DepartureAirportId, out var airport)) slot.DepartureAirport = airport;
                else throw new ArgumentException("Slot DepartureAirport Id not found: " + slot.DepartureAirportId);
                if (vatsimEvent.AirportsById.TryGetValue(slot.ArrivalAirportId, out airport)) slot.ArrivalAirport = airport;
                else throw new ArgumentException("Slot ArrivalAirport Id not found: " + slot.ArrivalAirportId);

                foreach (var routeSegmentId in slot.RouteSegmentIds)
                {
                    if (vatsimEvent.RouteSegmentsById.TryGetValue(routeSegmentId, out var routeSegment)) slot.RouteSegments.Add(routeSegment);
                    else throw new ArgumentException("Slot RouteSegment Id not found: " + routeSegmentId);
                }
            }
        }
        public static void WrapAllSlotAirportsAndRouteSegments(VATSIMEvent vatsimEvent)
        {
            foreach (var slot in vatsimEvent.Slots)
            {
                slot.DepartureAirportId = slot.DepartureAirport.Id;
                slot.ArrivalAirportId = slot.ArrivalAirport.Id;
                slot.RouteSegmentIds = slot.RouteSegments.Select(rs => rs.Id).ToList();
            }
        }
        public static void WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(VATSIMEvent vatsimEvent)
        {
            List<ThroughputPoint> throughputPoints = [.. vatsimEvent.Waypoints, .. vatsimEvent.RouteSegments, .. vatsimEvent.Airports, .. vatsimEvent.Sectors];
            foreach (var throughputPoint in throughputPoints)
            {
                foreach (var timeSlice in throughputPoint.AnalysisFramesViaMinutesFromSynchronizationTimeSlots)
                {
                    throughputPoint.AnalysisFramesViaMinutesFromSynchronizationTimeSlotIds[timeSlice.Key] = timeSlice.Value.Select(s => s.Id).ToList();
                }
            }
        }

        public static void WrapSlotHighVerbosityData(VATSIMEvent vatsimEvent)
        {
            foreach (var slot in vatsimEvent.Slots)
            {
                // combine route strings
                List<string> words = string.Join(' ', slot.RouteSegments.Select(rs => rs.RouteString)).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                for (int i = words.Count - 1; i > 0; i--)
                {
                    if (words[i - 1] == words[i]) words.RemoveAt(i);
                }
                slot.CombinedRouteString = string.Join(' ', words);
            }
        }
    }
}
