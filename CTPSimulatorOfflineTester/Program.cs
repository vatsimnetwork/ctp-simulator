using ConsoleTables;
using CTPSimulator;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;

namespace CTPSimulatorOfflineTester
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // set locale
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            var vatsimEvent = JsonConvert.DeserializeObject<VATSIMEvent>(File.ReadAllText("response_1774973748306.json"));
            vatsimEvent.CalculationParameters.HighVerbosity = true;
            vatsimEvent.CalculationParameters.HighSimulationAccuracy = true;
            vatsimEvent.CalculationParameters.CalculationFallbackGroundSpeed = 550;
            vatsimEvent.CalculationParameters.IntendedSlotGenerationMode = SimulatorCalculationParameters.SlotGenerationMode.MaximizeSlots;

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
                    var slots = vatsimEvent.Slots.FindAll(s => s.DepartureAirport == departureAirport && s.ArrivalAirport == arrivalAirport);
                    string cell = $"{slots.Count}";

                    // show number of routings for each city pair
                    if (slots.Count > 0)
                    {
                        int routings = slots.Select(s => string.Join('-', s.RouteSegments.Select(rs => rs.Id))).Distinct().Count();
                        cell += $" [{routings}]";
                    }

                    rowContent.Add(cell);
                }
                table.AddRow(rowContent.ToArray());
            }
            Console.WriteLine(table.ToString());
            foreach (var comment in vatsimEvent.CalculationParameters.SlotGenerationOutputComments) Console.WriteLine(comment);
            Console.WriteLine($"Slot calculation took {stopWatch.ElapsedMilliseconds}ms");
            JsonWrapping.WrapAllSlotAirportsAndRouteSegments(vatsimEvent);
            JsonWrapping.WrapSlotHighVerbosityData(vatsimEvent);

            var settings = JsonWrapping.SerializationSettings(JsonWrapping.Action.CreateSlotDistribution, true);
            settings.Formatting = Formatting.Indented;
            var vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, settings);
            File.WriteAllText("createSlotDistribution.json", vatsimEventJson);

            JsonWrapping.UnwrapAllRouteSegmentFacilityProgressions(vatsimEvent);

            var sectorBoundaries = await SectorParsing.LoadSectorBoundaries();
            JsonWrapping.UnwrapSectorBoundaries(vatsimEvent, sectorBoundaries);

            // event simulation
            Console.WriteLine();

            stopWatch = Stopwatch.StartNew();
            await Simulator.SimulateEvent(vatsimEvent);
            stopWatch.Stop();

            foreach (var comment in vatsimEvent.CalculationParameters.SimulationOutputComments) Console.WriteLine(comment);
            Console.WriteLine($"Event simulation took {stopWatch.ElapsedMilliseconds}ms");

            JsonWrapping.WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(vatsimEvent);
            settings = JsonWrapping.SerializationSettings(JsonWrapping.Action.SimulateEvent, true);
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
