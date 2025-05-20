using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TravelApp.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Globalization;
using static DataExporter;
using System.Text.Json;

[Route("api/[controller]")]
[ApiController]
public class RecommendationController : ControllerBase
{
    private readonly DataContext _context;
    private readonly ILogger<RecommendationController> _logger;

    public RecommendationController(DataContext context, ILogger<RecommendationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("train-model")]
    public IActionResult TrainModel()
    {
        try
        {
            var exporter = new DataExporter();
            string csvPath = "recommendation_data.csv";
            string modelPath = "place_model.zip";

            exporter.TrainModel(csvPath, modelPath);

            return Ok("✅ Mô hình đã được huấn luyện và lưu thành công.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Lỗi khi huấn luyện mô hình.");
            return StatusCode(500, "Lỗi khi huấn luyện mô hình: " + ex.Message);
        }
    }

    [HttpGet("export-training-data")]
    public async Task<IActionResult> ExportTrainingData()
    {
        var exporter = new DataExporter();
        await exporter.ExportTrainingDataAsync("recommendation_data.csv");

        return Ok("File training data đã được xuất.");
    }

    [HttpPost("suggest")]
    public async Task<ActionResult<IEnumerable<RecommendedPlaceDto>>> RecommendPlaces([FromBody] RecommendationRequest request)
    {
        if (request == null || request.AccountId <= 0)
        {
            _logger.LogWarning("Invalid recommendation request received.");
            return BadRequest("Invalid request data.");
        }

        _logger.LogInformation("Starting RecommendPlaces for AccountId: {AccountId}, Location: ({Latitude},{Longitude})",
            request.AccountId, request.Latitude, request.Longitude);

        var searchKeywords = _context.SearchHistory
            .Where(s => s.AccountId == request.AccountId)
            .OrderByDescending(s => s.SearchedAt)
            .Take(10)
            .Select(s => s.SearchQuery.ToLowerInvariant())
            .ToList();

        var favoriteCategories = _context.Favorite
            .Where(f => f.AccountId == request.AccountId && !string.IsNullOrEmpty(f.Category))
            .AsEnumerable()
            .GroupBy(f => f.Category.ToLowerInvariant())
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Select(g => g.Category)
            .ToList();

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "fsq3QCKaYXsYv/Ta0q9yvtlrIeb247rV7u6EtcMGX/oIEds=");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string latStr = request.Latitude.ToString(CultureInfo.InvariantCulture);
        string lonStr = request.Longitude.ToString(CultureInfo.InvariantCulture);
        string url = $"https://api.foursquare.com/v3/places/search?ll={latStr},{lonStr}&limit=50";

        var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var errContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Foursquare API error: {Status} - {Content}", response.StatusCode, errContent);
            return StatusCode((int)response.StatusCode, errContent);
        }

        var content = await response.Content.ReadAsStringAsync();
        var foursquarePlaces = ParseFoursquarePlaces(content);

        var exporter = new DataExporter();
        var recommendedPlaces = foursquarePlaces
            .Select(p =>
            {
                var input = new PlaceData
                {
                    Latitude = (float)p.Latitude,
                    Longitude = (float)p.Longitude,
                    Category = p.Category ?? "unknown",
                    Rating = 5.0f // giả định nếu không có
                };

                var predictedScore = exporter.PredictScore(input);

                var distance = GetDistance(request.Latitude, request.Longitude, p.Latitude, p.Longitude);
                var keywordScore = searchKeywords.Any(k => !string.IsNullOrEmpty(p.Name) && p.Name.ToLowerInvariant().Contains(k)) ? 1 : 0;
                var categoryScore = !string.IsNullOrEmpty(p.Category) && favoriteCategories.Contains(p.Category.ToLowerInvariant()) ? 1 : 0;

                return new RecommendedPlaceDto
                {
                    PlaceId = p.PlaceId,
                    Name = p.Name,
                    Address = p.Address,
                    Category = p.Category,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    Score = Math.Round(predictedScore + keywordScore + categoryScore - distance / 10.0, 2)
                };
            })
            .OrderByDescending(p => p.Score)
            .Take(10)
            .ToList();

        _logger.LogInformation("Recommendation finished, returning {Count} places.", recommendedPlaces.Count);
        return Ok(recommendedPlaces);
    }

    private List<Place> ParseFoursquarePlaces(string jsonContent)
    {
        var results = new List<Place>();
        using var jsonDoc = JsonDocument.Parse(jsonContent);
        var root = jsonDoc.RootElement;
        var resultsArray = root.GetProperty("results");

        int idCounter = 1;
        foreach (var item in resultsArray.EnumerateArray())
        {
            var location = item.GetProperty("geocodes").GetProperty("main");
            var address = item.TryGetProperty("location", out var loc) && loc.TryGetProperty("formatted_address", out var addr) ? addr.GetString() : "";

            results.Add(new Place
            {
                PlaceId = idCounter++,
                Name = item.GetProperty("name").GetString(),
                Address = address,
                Category = item.TryGetProperty("categories", out var cats) && cats.GetArrayLength() > 0 ? cats[0].GetProperty("name").GetString() : "other",
                Latitude = location.GetProperty("latitude").GetDouble(),
                Longitude = location.GetProperty("longitude").GetDouble()
            });
        }

        return results;
    }

    public class Place
    {
        public int PlaceId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Category { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private double GetDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private double ToRadians(double angle) => angle * Math.PI / 180;
}
