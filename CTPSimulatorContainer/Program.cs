using CTPSimulator;
using EmbedIO;
using EmbedIO.Actions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;

namespace CTPSimulatorContainer
{
    internal class Program
    {
        static Dictionary<string, List<SectorBoundary>> SectorBoundaries;

        static async Task Main(string[] args)
        {
            // set locale
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            // load sectors
            SectorBoundaries = await SectorParsing.LoadSectorBoundaries();

            // start the webserver
            using (var server = new WebServer("http://*:8080")
                .WithModule(new ActionModule("/createSlotDistribution", HttpVerbs.Any, CreateSlotDistribution))
                .WithModule(new ActionModule("/simulateEvent", HttpVerbs.Any, SimulateEvent)))
            {
                await server.RunAsync();
                await Task.Delay(-1);
            }
        }

        private static VATSIMEvent extractVatsimEvent(IHttpContext context)
        {
            try
            {
                var request = context.Request;
                if (request.HasEntityBody)
                {
                    Stream body = request.InputStream;
                    string json = new StreamReader(body, request.ContentEncoding).ReadToEnd();
                    if (!string.IsNullOrEmpty(json))
                    {
                        VATSIMEvent vatsimEvent = JsonConvert.DeserializeObject<VATSIMEvent>(json);
                        if (vatsimEvent != null) return vatsimEvent;  
                    }
                }
                throw new NullReferenceException("ExtractVatsimEvent returned null.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error extracting vatsim event from JSON.");
                Console.WriteLine(ex);
                throw;
            }
        }
        private static async Task SerializeAndSendVATSIMEvent(VATSIMEvent vatsimEvent, IHttpContext context, JsonSerializerSettings serializerSettings)
        {
            var vatsimEventJson = JsonConvert.SerializeObject(vatsimEvent, serializerSettings);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            await context.SendStringAsync(vatsimEventJson, "application/json", System.Text.Encoding.UTF8);
        }

        private static async Task CreateSlotDistribution(IHttpContext context)
        {
            try
            {
                var vatsimEvent = extractVatsimEvent(context);
                JsonWrapping.UnwrapAllRouteSegmentData(vatsimEvent);
                await SlotDistributionCreator.CreateSlotDistribution(vatsimEvent);
                JsonWrapping.WrapAllSlotAirportsAndRouteSegments(vatsimEvent);
                if (vatsimEvent.CalculationParameters.HighVerbosity) JsonWrapping.WrapSlotHighVerbosityData(vatsimEvent);
                await SerializeAndSendVATSIMEvent(vatsimEvent, context, JsonWrapping.SerializationSettings(JsonWrapping.Action.CreateSlotDistribution, vatsimEvent.CalculationParameters.HighVerbosity));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating slot distribution.");
                Console.WriteLine(ex);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.SendStringAsync(ex.ToString(), "text/plain", System.Text.Encoding.UTF8);
            }
        }
        private static async Task SimulateEvent(IHttpContext context)
        {
            try
            {
                var vatsimEvent = extractVatsimEvent(context);
                if (vatsimEvent.CalculationParameters.IntendedDepartureTimeWindowOffsetsCalculationMode != SimulatorCalculationParameters.DepartureTimeWindowOffsetsCalculationMode.None)
                {
                    JsonWrapping.UnwrapAirportPairDepartureTimeWindowShiftingsIds(vatsimEvent);
                }    
                JsonWrapping.UnwrapAllRouteSegmentData(vatsimEvent);
                JsonWrapping.UnwrapAllSlotAirportsAndRouteSegments(vatsimEvent);
                JsonWrapping.UnwrapSectorBoundaries(vatsimEvent, SectorBoundaries);

                // simulate
                await Simulator.SimulateEvent(vatsimEvent);
                JsonWrapping.WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(vatsimEvent);
                if (vatsimEvent.CalculationParameters.HighVerbosity) JsonWrapping.WrapSlotHighVerbosityData(vatsimEvent);
                await SerializeAndSendVATSIMEvent(vatsimEvent, context, JsonWrapping.SerializationSettings(JsonWrapping.Action.SimulateEvent, vatsimEvent.CalculationParameters.HighVerbosity));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error simulating event.");
                Console.WriteLine(ex);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.SendStringAsync(ex.ToString(), "text/plain", System.Text.Encoding.UTF8);
            }
        }
    }
}
