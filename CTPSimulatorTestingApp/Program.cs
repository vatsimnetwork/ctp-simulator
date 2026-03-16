using ConsoleTables;
using CTPSimulator;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace CTPSimulatorTestingApp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var vatsimEvent = TestingDataLoader.Load("25W");

            Stopwatch stopWatch = Stopwatch.StartNew();
            await SlotDistributionCreator.CreateSlotDistribution(vatsimEvent);
            stopWatch.Stop();

            var table = new ConsoleTable("Departures", "NAT Tracks", "Arrivals");
            table.Options.EnableCount = false;

            var natSegments = vatsimEvent.RouteSegments.Where(r => r.Type == RouteSegmentType.NAT).ToList();
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

            Console.WriteLine($"Calculation took {stopWatch.ElapsedMilliseconds}ms");

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }
        static string BuildInfo(ThroughputPoint throughputPoint)
        {
            return $"{throughputPoint.Identifier} {throughputPoint.SlotsAllocated} / {throughputPoint.MaximumSlots}";
        }
    }
}
