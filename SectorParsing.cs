using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CTPSimulator
{
    public static class SectorParsing
    {
        public static async Task<List<Sector>> LoadSectors(string pathToBoundariesGeoJSON = null)
        {
            string json;
            if (pathToBoundariesGeoJSON == null)
            {
                string resourceName = "CTPSimulator.Resources.Boundaries.geojson";
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                using StreamReader reader = new StreamReader(stream);
                json = reader.ReadToEnd();
            }
            else
            {
                json = File.ReadAllText(pathToBoundariesGeoJSON);
            }
            List<Sector> sectors = new List<Sector>();
            JObject boundariesJSON = JObject.Parse(json);
            foreach (JToken boundary in boundariesJSON["features"])
            {
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
                string code = (boundary["properties"]["id"]).ToString();
                sectors.Add(new Sector
                {
                    Identifier = code,
                    SectorBoundaries = boundaries
                });
            }
            return sectors;
        }
    }
}
