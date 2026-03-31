using ConsoleTables;
using CTPSimulator;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace CTPSimulatorOfflineTester
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var vatsimEvent = JsonConvert.DeserializeObject<VATSIMEvent>(File.ReadAllText("response_1774921370528.json"));
            JsonWrapping.UnwrapAllRouteSegmentLocations(vatsimEvent);

            //var vatsimEvent = TestingDataLoader.Load("25W");
            //vatsimEvent.CalculationParameters.CalculateThroughputDataOnlyForManuallyProvidedSectors = false;
            //vatsimEvent.Date = new DateOnly(2025, 04, 26);
            //vatsimEvent.Sectors = await SectorParsing.LoadSectors();

            // slot calculation
            Stopwatch stopWatch = Stopwatch.StartNew();
            await SlotDistributionCreator.CreateSlotDistribution(vatsimEvent);
            stopWatch.Stop();

            // display the results
            var table = new ConsoleTable("Departures", "NAT Tracks", "Arrivals");
            table.Options.EnableCount = false;

            var natSegments = vatsimEvent.RouteSegments.Where(rs => vatsimEvent.Airports.Exists(a => a.ConnectingSecondaryRouteSegments.Contains(rs))).ToList();
            int rows = Math.Max(Math.Max(vatsimEvent.DepartureAirports.Count, natSegments.Count), vatsimEvent.ArrivalAirports.Count);
            for (int i = 0; i < rows; i++)
            {
                table.AddRow(
                    i < vatsimEvent.DepartureAirports.Count ? BuildInfo(vatsimEvent.DepartureAirports[i]) : string.Empty,
                    i < natSegments.Count ? BuildInfo(natSegments[i]) : string.Empty,
                    i < vatsimEvent.ArrivalAirports.Count ? BuildInfo(vatsimEvent.ArrivalAirports[i]) : string.Empty);
            }

            table.AddRow(
                $"{vatsimEvent.DepartureAirports.Sum(da => da.SlotsAllocated)} / {vatsimEvent.DepartureAirports.Sum(da => da.MaximumSlots)}",
                $"{natSegments.Sum(sr => sr.SlotsAllocated)} / {natSegments.Sum(sr => sr.MaximumSlots)}",
                $"{vatsimEvent.ArrivalAirports.Sum(aa => aa.SlotsAllocated)} / {vatsimEvent.ArrivalAirports.Sum(aa => aa.MaximumSlots) }");

            Console.WriteLine(table.ToString());

            // display departure / arrival slots
            table = new ConsoleTable([string.Empty, .. vatsimEvent.ArrivalAirports.Select(da => da.Identifier)]);
            table.Options.EnableCount = false;

            foreach (var departureAirport in vatsimEvent.DepartureAirports)
            {
                List<string> rowContent = [departureAirport.Identifier];
                foreach (var arrivalAirport in vatsimEvent.ArrivalAirports)
                {
                    rowContent.Add(vatsimEvent.Slots.Count(s => s.DepartureAirportInternal == departureAirport && s.ArrivalAirportInternal == arrivalAirport).ToString());
                }
                table.AddRow(rowContent.ToArray());
            }
            Console.WriteLine(table.ToString());

            Console.WriteLine(vatsimEvent.CalculationParameters.SlotGenerationOutputCommentary);
            Console.WriteLine($"Slot calculation took {stopWatch.ElapsedMilliseconds}ms");

            JsonWrapping.WrapAllSlotAirportsAndRouteSegments(vatsimEvent);
            JsonWrapping.WrapSlotGenerationOutputCommentary(vatsimEvent);
            var settings = JsonWrapping.CreateSlotDistributionSerializationSettings;
            settings.Formatting = Formatting.Indented;
            var vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, settings);
            File.WriteAllText("createSlotDistribution.json", vatsimEventJson);

            JsonWrapping.UnwrapAllRouteSegmentFacilityProgressions(vatsimEvent);

            var sectorBoundaries = await SectorParsing.LoadSectorBoundaries();
            JsonWrapping.UnwrapSectorBoundaries(vatsimEvent, sectorBoundaries);

            // event simulation
            stopWatch = Stopwatch.StartNew();
            await Simulator.SimulateEvent(vatsimEvent);
            stopWatch.Stop();

            Console.WriteLine(vatsimEvent.CalculationParameters.SimulationOutputCommentary);
            Console.WriteLine($"Event simulation took {stopWatch.ElapsedMilliseconds}ms");

            JsonWrapping.WrapSimulationOutputCommentary(vatsimEvent);
            JsonWrapping.WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(vatsimEvent);
            settings = JsonWrapping.SimulateEventSerializationSettings;
            settings.Formatting = Formatting.Indented;
            vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, settings);
            File.WriteAllText("simulateEvent.json", vatsimEventJson);

            // Block this task until the program is closed.
           await Task.Delay(-1);
        }
        static string BuildInfo(ThroughputPoint throughputPoint)
        {
            return $"{throughputPoint.Identifier} {throughputPoint.SlotsAllocated} / {throughputPoint.MaximumSlots}";
        }
    }
}
