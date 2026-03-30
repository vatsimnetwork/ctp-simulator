using CTPSimulator;
using EmbedIO;
using EmbedIO.Actions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace CTPSimulatorContainer
{
    internal class Program
    {
        static List<Sector> Sectors;

        static async Task Main(string[] args)
        {
            // load sectors
            Sectors = await SectorParsing.LoadSectors();

            // start the webserver
            using (var server = new WebServer()
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
                JsonWrapping.UnwrapAllRouteSegmentLocations(vatsimEvent);
                await SlotDistributionCreator.CreateSlotDistribution(vatsimEvent);
                JsonWrapping.WrapAllSlotAirportsAndRouteSegments(vatsimEvent);
                await SerializeAndSendVATSIMEvent(vatsimEvent, context, JsonWrapping.CreateSlotDistributionSerializationSettings);
            }
            catch (Exception ex)
            {
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
                JsonWrapping.UnwrapAllRouteSegmentLocations(vatsimEvent);
                JsonWrapping.UnwrapAllRouteSegmentFacilityProgressions(vatsimEvent);
                JsonWrapping.UnwrapAllSlotAirportsAndRouteSegments(vatsimEvent);
                await Simulator.SimulateEvent(vatsimEvent);
                JsonWrapping.WrapAllThroughputPointSlotsAnalysisFramesViaMinutesFromSynchronizationTimes(vatsimEvent);
                await SerializeAndSendVATSIMEvent(vatsimEvent, context, JsonWrapping.SimulateEventSerializationSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.SendStringAsync(ex.ToString(), "text/plain", System.Text.Encoding.UTF8);
            }
        }
    }
}
