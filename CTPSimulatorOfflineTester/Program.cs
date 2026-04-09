using ConsoleTables;
using CTPSimulator;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;

namespace CTPSimulatorOfflineTester
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // set locale
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            //var vatsimEvent = ReadVATSIMEventFromJson("response_1775663756224.json", false);
            //await CreateSlotDistribution(vatsimEvent);
            //SaveSlotDistribution(vatsimEvent);

            Console.WriteLine();

            // event simulation
            var vatsimEvent = ReadVATSIMEventFromJson("response_1775766807652.json", true);
            //vatsimEvent.CalculationParameters.HighSimulationAccuracy = true;
            //vatsimEvent.CalculationParameters.HighVerbosity = true;
            //vatsimEvent.CalculationParameters.CalculationFallbackGroundSpeed = 550;
            await SimulateEvent(vatsimEvent);
            SaveEventSimulation(vatsimEvent);
        }


        static VATSIMEvent ReadVATSIMEventFromJson(string jsonName, bool unwrapAllSlotAirportsAndRouteSegments)
        {
            VATSIMEvent vatsimEvent = JsonConvert.DeserializeObject<VATSIMEvent>(File.ReadAllText(jsonName));
            JsonWrapping.UnwrapAllRouteSegmentData(vatsimEvent);
            if (unwrapAllSlotAirportsAndRouteSegments) JsonWrapping.UnwrapAllSlotAirportsAndRouteSegments(vatsimEvent);
            return vatsimEvent;
        }
        static VATSIMEvent ReadVATSIMEventFromCSVs()
        {
            var vatsimEvent = TestingDataLoader.Load("25W");
            vatsimEvent.CalculationParameters.CalculateThroughputDataOnlyForManuallyProvidedSectors = false;
            vatsimEvent.Date = new DateOnly(2025, 04, 26);
            return vatsimEvent;
        }
        static async Task CreateSlotDistribution(VATSIMEvent vatsimEvent)
        {
            // slot calculation
            Stopwatch stopWatch = Stopwatch.StartNew();
            await SlotDistributionCreator.CreateSlotDistribution(vatsimEvent);
            stopWatch.Stop();

            // display the results
            var table = new ConsoleTable("Departures", "Arrivals");
            table.Options.EnableCount = false;

            int rows = new List<int>([vatsimEvent.DepartureAirports.Count, vatsimEvent.ArrivalAirports.Count]).Max();
            for (int i = 0; i < rows; i++)
            {
                table.AddRow(
                    i < vatsimEvent.DepartureAirports.Count ? BuildInfo(vatsimEvent.DepartureAirports[i]) : string.Empty,
                    i < vatsimEvent.ArrivalAirports.Count ? BuildInfo(vatsimEvent.ArrivalAirports[i]) : string.Empty);
            }

            table.AddRow(
                $"{vatsimEvent.DepartureAirports.Sum(da => da.SlotsAllocated)} / {vatsimEvent.DepartureAirports.Sum(da => da.MaximumSlots)}",
                $"{vatsimEvent.ArrivalAirports.Sum(aa => aa.SlotsAllocated)} / {vatsimEvent.ArrivalAirports.Sum(aa => aa.MaximumSlots)}");

            Console.WriteLine(table.ToString());

            // routes, sectors and route tags
            table = new ConsoleTable("Routes", "Route Tags", "Sectors");
            rows = Math.Max(vatsimEvent.RouteSegments.Count, Math.Max(vatsimEvent.Sectors.Count, vatsimEvent.TagLimits.Count));
            for (int i = 0; i < rows; i++)
            {
                table.AddRow(
                  i < vatsimEvent.RouteSegments.Count ? BuildInfo(vatsimEvent.RouteSegments[i]) : string.Empty,
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
            Console.WriteLine();
            Console.WriteLine($"Slot generation took {stopWatch.ElapsedMilliseconds}ms");
        }
        static void SaveSlotDistribution(VATSIMEvent vatsimEvent)
        {
            JsonWrapping.WrapAllSlotAirportsAndRouteSegments(vatsimEvent);
            if (vatsimEvent.CalculationParameters.HighVerbosity) JsonWrapping.WrapSlotHighVerbosityData(vatsimEvent);

            var settings = JsonWrapping.SerializationSettings(JsonWrapping.Action.CreateSlotDistribution, true);
            settings.Formatting = Formatting.Indented;
            var vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, settings);
            File.WriteAllText("createSlotDistribution.json", vatsimEventJson);
        }
        static async Task SimulateEvent(VATSIMEvent vatsimEvent)
        {
            var sectorBoundaries = await SectorParsing.LoadSectorBoundaries();
            JsonWrapping.UnwrapSectorBoundaries(vatsimEvent, sectorBoundaries);

            var stopWatch = Stopwatch.StartNew();
            await Simulator.SimulateEvent(vatsimEvent);
            stopWatch.Stop();

            foreach (var comment in vatsimEvent.CalculationParameters.SimulationOutputComments) Console.WriteLine(comment);
            Console.WriteLine($"Event simulation took {stopWatch.ElapsedMilliseconds}ms");
        }
        static void SaveEventSimulation(VATSIMEvent vatsimEvent)
        {
            JsonWrapping.WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(vatsimEvent);
            if (vatsimEvent.CalculationParameters.HighVerbosity) JsonWrapping.WrapSlotHighVerbosityData(vatsimEvent);
            var settings = JsonWrapping.SerializationSettings(JsonWrapping.Action.SimulateEvent, true);
            settings.Formatting = Formatting.Indented;
            string vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, settings);
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
