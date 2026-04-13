using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Reflection;
using System.Text;

namespace CTPSimulator
{
    public static class SectorParsing
    {
        /// <summary>Attention: These are in a priority order. If the a sector with the same code is defined in multiple files, only the one from the highest file will be kept.</summary>
        private static readonly string[] SectorLinks =
        [
            "https://raw.githubusercontent.com/vatsimnetwork/vatspy-data-project/master/Boundaries.geojson",
            //"https://raw.githubusercontent.com/vATCSCC/PERTI/refs/heads/main/assets/geojson/high.json",
        ];


        public static async Task<Dictionary<string, List<SectorBoundary>>> DownloadSectorBoundaries(bool fileCaching)
        {
            Dictionary<string, List<SectorBoundary>> sectorBoundaries = new();
            var directory = Directory.CreateDirectory("Boundaries");
            using var httpClient = new HttpClient();
            HashSet<string> allDefinedCodes = new();

            foreach (var link in SectorLinks)
            {
                var uri = new Uri(link);               
                string filePath = Path.Combine(directory.FullName, Path.GetFileName(uri.LocalPath));

                string json;
                if (fileCaching && File.Exists(filePath)) json = File.ReadAllText(filePath);
                else
                {
                    json = await httpClient.GetStringAsync(uri);
                    if (fileCaching) File.WriteAllText(filePath, json);
                }

                HashSet<string> fileDefinedCodes = new();

                JObject boundariesJSON = JObject.Parse(json);
                foreach (JToken boundary in boundariesJSON["features"])
                {
                    var idJson = boundary["properties"]["id"];
                    if (idJson == null) idJson = boundary["properties"]["label"];
                    string code = idJson.ToString();

                    if (allDefinedCodes.Contains(code)) continue; // skip if defined in previous file
                    fileDefinedCodes.Add(code);

                    List<SectorBoundary> boundaries = new List<SectorBoundary>();
                    foreach (JToken polygon in boundary["geometry"]["coordinates"])
                    {
                        List<JToken> coordinatesList = (polygon[0]).ToList();
                        double[,] coordinatesArray = new double[coordinatesList.Count, 2];
                        double minLat = double.MaxValue;
                        double maxLat = double.MinValue;
                        double minLon = double.MaxValue;
                        double maxLon = double.MinValue;
                        for (int i = 0; i < coordinatesList.Count; i++)
                        {
                            double lat = (double)coordinatesList[i][1];
                            double lon = (double)coordinatesList[i][0];
                            if (lat < minLat)
                            {
                                minLat = lat;
                            }
                            if (lat > maxLat)
                            {
                                maxLat = lat;
                            }
                            if (lon < minLon)
                            {
                                minLon = lon;
                            }
                            if (lon > maxLon)
                            {
                                maxLon = lon;
                            }
                            coordinatesArray[i, 0] = lat;
                            coordinatesArray[i, 1] = lon;
                        }
                        boundaries.Add(new SectorBoundary
                        {
                            Coordinates = coordinatesArray,
                            MaxLatitude = maxLat,
                            MinLatitude = minLat,
                            MinLongitude = minLon,
                            MaxLongitude = maxLon
                        });
                    }

                    if (sectorBoundaries.TryGetValue(code, out var definedBoundaries))
                    {
                        definedBoundaries.AddRange(boundaries);
                    }
                    else sectorBoundaries.Add(code, boundaries);
                }

                foreach (var code in fileDefinedCodes) allDefinedCodes.Add(code);
            }
 
            return sectorBoundaries;
        }
    }
}
