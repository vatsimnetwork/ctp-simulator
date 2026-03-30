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
        public static readonly JsonSerializerSettings CreateSlotDistributionSerializationSettings = new() { ContractResolver = new JsonCreateSlotDistributionPropertiesResolver() };
        public static readonly JsonSerializerSettings SimulateEventSerializationSettings = new() { ContractResolver = new JsonSimulateEventPropertiesResolver() };
        public class JsonIgnoreSerializationAttribute : Attribute { }
        public class JsonIgnoreCreateSlotDistributionSerializationAttribute : Attribute { }
        class JsonCreateSlotDistributionPropertiesResolver : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                //Return properties that do NOT have the JsonIgnoreSerializationAttribute
                return objectType.GetProperties()
                                 .Where(pi => !Attribute.IsDefined(pi, typeof(JsonIgnoreSerializationAttribute)) &&
                                 !Attribute.IsDefined(pi, typeof(JsonIgnoreCreateSlotDistributionSerializationAttribute)))
                                 .ToList<MemberInfo>();
            }
        }
        public class JsonIgnoreSimulateEventSerializationAttribute : Attribute { }
        class JsonSimulateEventPropertiesResolver : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                //Return properties that do NOT have the JsonIgnoreSerializationAttribute
                return objectType.GetProperties()
                                 .Where(pi => !Attribute.IsDefined(pi, typeof(JsonIgnoreSerializationAttribute)) &&
                                 !Attribute.IsDefined(pi, typeof(JsonIgnoreSimulateEventSerializationAttribute)))
                                 .ToList<MemberInfo>();
            }
        }

        public static void UnwrapAllRouteSegmentLocations(VATSIMEvent vatsimEvent)
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

            // unwrap ids
            foreach (var routeSegment in vatsimEvent.RouteSegments)
            {
                foreach (var locationId in routeSegment.Locations)
                {
                    if (vatsimEvent.AirportsById.TryGetValue(locationId, out var airport)) routeSegment.LocationsInternal.Add(airport);
                    else if (vatsimEvent.WaypointsById.TryGetValue(locationId, out var waypoint)) routeSegment.LocationsInternal.Add(waypoint);
                    else throw new ArgumentException("RouteSegment location Id not found: " + locationId);
                }
            }
        }
        public static void UnwrapAllRouteSegmentFacilityProgressions(VATSIMEvent vatsimEvent)
        {
            foreach (var sector in vatsimEvent.Sectors)
            {
                if (vatsimEvent.SectorsById.ContainsKey(sector.Id)) throw new ArgumentException("Duplicate sector Id defined: " + sector.Id);
                vatsimEvent.SectorsById[sector.Id] = sector;
            }

            foreach (var routeSegment in vatsimEvent.RouteSegments)
            {
                foreach (var sectorId in routeSegment.ProvidedFacilityProgression)
                {
                    if (vatsimEvent.SectorsById.TryGetValue(sectorId, out var sector)) routeSegment.ProvidedFacilityProgressionInternal.Add(sector);
                    else throw new ArgumentException("RouteSegment ProvidedFacilityProgression sector Id not found: " + sectorId);
                }
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
                if (vatsimEvent.AirportsById.TryGetValue(slot.DepartureAirport, out var airport)) slot.DepartureAirportInternal = airport;
                else throw new ArgumentException("Slot DepartureAirport Id not found: " + slot.DepartureAirport);
                if (vatsimEvent.AirportsById.TryGetValue(slot.ArrivalAirport, out airport)) slot.ArrivalAirportInternal = airport;
                else throw new ArgumentException("Slot ArrivalAirport Id not found: " + slot.ArrivalAirport);

                foreach (var routeSegmentId in slot.RouteSegments)
                {
                    if (vatsimEvent.RouteSegmentsById.TryGetValue(routeSegmentId, out var routeSegment)) slot.RouteSegmentsInternal.Add(routeSegment);
                    else throw new ArgumentException("Slot RouteSegment Id not found: " + routeSegmentId);
                }
            }
        }
        public static void WrapAllSlotAirportsAndRouteSegments(VATSIMEvent vatsimEvent)
        {
            foreach (var slot in vatsimEvent.Slots)
            {
                slot.DepartureAirport = slot.DepartureAirportInternal.Id;
                slot.ArrivalAirport = slot.ArrivalAirportInternal.Id;
                slot.RouteSegments = slot.RouteSegmentsInternal.Select(rs => rs.Id).ToList();
            }
        }
        public static void WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(VATSIMEvent vatsimEvent)
        {
            List<ThroughputPoint> throughputPoints = [.. vatsimEvent.Waypoints, .. vatsimEvent.RouteSegments, .. vatsimEvent.Airports];
            foreach (var throughputPoint in throughputPoints)
            {
                foreach (var timeSlice in throughputPoint.SlotsAnalysisFramesViaMinutesFromSynchronizationTimeInternal)
                {
                    throughputPoint.SlotsAnalysisFramesViaMinutesFromSynchronizationTime[timeSlice.Key] = timeSlice.Value.Select(s => s.Id).ToList();
                }
            }
        }
    }
}
