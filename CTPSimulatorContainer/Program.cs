using CTPSimulator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace CTPSimulatorContainer
{
    internal class Program
    {
        const int Port = 8080;
        static readonly HttpListener Listener = new();

        static async Task Main(string[] args)
        {
            // start the web server
            Listener.Prefixes.Add($"http://+:{Port}/");
            Listener.Start();
            Listen();
            await Task.Delay(-1);
        }

        private static void Listen()
        {
            Listener.BeginGetContext(new AsyncCallback(ListenerCallback), Listener);
        }
        private static void ListenerCallback(IAsyncResult result)
        {
            var context = Listener.EndGetContext(result);
            var request = context.Request;

            string responseJson = string.Empty;
            if (request.HasEntityBody)
            {
                Stream body = request.InputStream;
                System.Text.Encoding encoding = request.ContentEncoding;
                string inputJson = new StreamReader(body, encoding).ReadToEnd();

                if (!string.IsNullOrEmpty(inputJson))
                {
                    VATSIMEvent vatsimEvent = JsonConvert.DeserializeObject<VATSIMEvent>(inputJson);

                    // do the calculations
                    if (request.Url.AbsolutePath.Contains("CreateSlotDistribution", StringComparison.InvariantCultureIgnoreCase))
                    {
                        // create slot distribution

                    }
                    else if (request.Url.AbsolutePath.Contains("SimulateEvent", StringComparison.InvariantCultureIgnoreCase))
                    {
                        // simulate event

                    }
                }            
            }    

            var response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            Listen();
        }
    }
}
