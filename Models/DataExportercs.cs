using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CsvHelper;
using Newtonsoft.Json.Linq;

public class TrainingData
{
    public int AccountId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Category { get; set; }
    public float Rating { get; set; }
    public int Label { get; set; } // 1 = Favorite, 0 = Not favorite
}

public class DataExporter
{
    private readonly string _connectionString = "Data Source=LAP-CUA-BOTHOMT\\NEWBOSERVER;Initial Catalog=SmartTravelApp;Integrated Security=True;TrustServerCertificate=True;";
    private readonly string _foursquareApiKey = "fsq3QCKaYXsYv/Ta0q9yvtlrIeb247rV7u6EtcMGX/oIEds=";

    public async Task ExportTrainingDataAsync(string outputCsvPath)
    {
        var data = new List<TrainingData>();

        // 1. Lấy dữ liệu yêu thích (Label = 1)
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            var cmd = new SqlCommand("SELECT AccountId, Latitude, Longitude, Category, Rating FROM Favorite WHERE Latitude IS NOT NULL AND Longitude IS NOT NULL", conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                data.Add(new TrainingData
                {
                    AccountId = reader.GetInt32(0),
                    Latitude = reader.GetDouble(1),
                    Longitude = reader.GetDouble(2),
                    Category = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                    Rating = reader.IsDBNull(4) ? 0 : (float)reader.GetDouble(4),
                    Label = 1
                });
            }
        }

        // 2. Lấy ngẫu nhiên các địa điểm từ Foursquare (Label = 0)
        foreach (var group in data.GroupBy(d => d.AccountId))
        {
            var lat = group.First().Latitude;
            var lon = group.First().Longitude;

            var notFavPlaces = await GetFoursquarePlacesAsync(lat, lon, excludeCategories: group.Select(x => x.Category).ToList());

            foreach (var place in notFavPlaces)
            {
                data.Add(new TrainingData
                {
                    AccountId = group.Key,
                    Latitude = place.lat,
                    Longitude = place.lon,
                    Category = place.category,
                    Rating = place.rating,
                    Label = 0
                });
            }
        }

        // 3. Ghi ra file CSV
        using var writer = new StreamWriter(outputCsvPath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(data);

        Console.WriteLine($"✅ Exported {data.Count} records to {outputCsvPath}");
    }

    private async Task<List<(double lat, double lon, string category, float rating)>> GetFoursquarePlacesAsync(
    double latitude, double longitude, List<string> excludeCategories)
    {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", _foursquareApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // ✅ Format tọa độ đúng
        var latStr = latitude.ToString(CultureInfo.InvariantCulture);
        var lonStr = longitude.ToString(CultureInfo.InvariantCulture);
        var url = $"https://api.foursquare.com/v3/places/search?ll={latStr},{lonStr}&radius=3000&limit=10";

        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Foursquare API error: {response.StatusCode} - {errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var places = new List<(double, double, string, float)>();

        var obj = JObject.Parse(json);
        var results = obj["results"];

        foreach (var item in results)
        {
            var category = item["categories"]?.FirstOrDefault()?["name"]?.ToString() ?? "Unknown";
            if (excludeCategories.Contains(category)) continue;

            var lat = item["geocodes"]?["main"]?["latitude"]?.Value<double>() ?? 0;
            var lon = item["geocodes"]?["main"]?["longitude"]?.Value<double>() ?? 0;

            if (lat != 0 && lon != 0)
            {
                places.Add((lat, lon, category, 5.0f));
            }
        }

        return places;
    }

}
