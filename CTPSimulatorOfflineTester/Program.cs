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

            JsonWrapping.UnwrapAllRouteSegmentData(vatsimEvent);

            //var vatsimEvent = TestingDataLoader.Load("25W");
            //vatsimEvent.CalculationParameters.CalculateThroughputDataOnlyForManuallyProvidedSectors = false;
            //vatsimEvent.Date = new DateOnly(2025, 04, 26);
            //vatsimEvent.Sectors = await SectorParsing.LoadSectors();

            // slot calculation
            Stopwatch stopWatch = Stopwatch.StartNew();
            await SlotDistributionCreator.CreateSlotDistribution(vatsimEvent);
            stopWatch.Stop();

            // display the results
            var table = new ConsoleTable("Departures", "Primary", "NAT Tracks", "Tertiary", "Arrivals");
            table.Options.EnableCount = false;

            var primarySegments = vatsimEvent.DepartureAirports.SelectMany(da => da.ConnectingPrimaryRouteSegments).ToList();
            var natSegments = vatsimEvent.RouteSegments.Where(rs => vatsimEvent.Airports.Exists(a => a.ConnectingSecondaryRouteSegments.Contains(rs))).ToList();
            var tertiarySegments = vatsimEvent.ArrivalAirports.SelectMany(aa => aa.ConnectingPrimaryRouteSegments).ToList();

            int rows = new List<int>([vatsimEvent.DepartureAirports.Count, primarySegments.Count, natSegments.Count, tertiarySegments.Count, vatsimEvent.ArrivalAirports.Count]).Max();
            for (int i = 0; i < rows; i++)
            {
                table.AddRow(
                    i < vatsimEvent.DepartureAirports.Count ? BuildInfo(vatsimEvent.DepartureAirports[i]) : string.Empty,
                    i < primarySegments.Count ? BuildInfo(primarySegments[i]) : string.Empty,
                    i < natSegments.Count ? BuildInfo(natSegments[i]) : string.Empty,
                    i < tertiarySegments.Count ? BuildInfo(tertiarySegments[i]) : string.Empty,
                    i < vatsimEvent.ArrivalAirports.Count ? BuildInfo(vatsimEvent.ArrivalAirports[i]) : string.Empty);
            }

            table.AddRow(
                $"{vatsimEvent.DepartureAirports.Sum(da => da.SlotsAllocated)} / {vatsimEvent.DepartureAirports.Sum(da => da.MaximumSlots)}",
                string.Empty,
                $"{natSegments.Sum(s => s.SlotsAllocated)} / {natSegments.Sum(s => s.MaximumSlots)}",
                string.Empty,
                $"{vatsimEvent.ArrivalAirports.Sum(aa => aa.SlotsAllocated)} / {vatsimEvent.ArrivalAirports.Sum(aa => aa.MaximumSlots) }");

            Console.WriteLine(table.ToString());

            // sectors and route tags
            table = new ConsoleTable("Route Tags", "Sectors");
            rows = Math.Max(vatsimEvent.Sectors.Count, vatsimEvent.TagLimits.Count);
            for (int i = 0; i < rows; i++)
            {
                table.AddRow(
                  i < vatsimEvent.TagLimits.Count ? BuildInfo(vatsimEvent.TagLimits[i]) : string.Empty,
                  i < vatsimEvent.Sectors.Count ? BuildInfo(vatsimEvent.Sectors[i]) : string.Empty);
            }
            table.Options.EnableCount = false;
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
            Console.WriteLine($"Slot generation took {stopWatch.ElapsedMilliseconds}ms");
            JsonWrapping.WrapAllSlotAirportsAndRouteSegments(vatsimEvent);
            JsonWrapping.WrapSlotHighVerbosityData(vatsimEvent);

            var settings = JsonWrapping.SerializationSettings(JsonWrapping.Action.CreateSlotDistribution, true);
            settings.Formatting = Formatting.Indented;
            var vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, settings);
            File.WriteAllText("createSlotDistribution.json", vatsimEventJson);

            // event simulation
            Console.WriteLine();

            var sectorBoundaries = await SectorParsing.LoadSectorBoundaries();
            JsonWrapping.UnwrapSectorBoundaries(vatsimEvent, sectorBoundaries);

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
        }
        static string BuildInfo(ThroughputPoint throughputPoint)
        {
            string output = $"{throughputPoint.Identifier.Split(' ').First()} {throughputPoint.SlotsAllocated}";
            if (throughputPoint.MaximumAircraftPerHour < ThroughputPoint.InfinityMarker) output += $" / {throughputPoint.MaximumSlots}";
            return output;
        }
    }
}
