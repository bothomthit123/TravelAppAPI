using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CsvHelper;
using Newtonsoft.Json.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;

public class DataExporter
{
    private readonly string _connectionString = "Data Source=LAP-CUA-BOTHOMT\\NEWBOSERVER;Initial Catalog=SmartTravelApp;Integrated Security=True;TrustServerCertificate=True;";
    private readonly string _foursquareApiKey = "fsq3QCKaYXsYv/Ta0q9yvtlrIeb247rV7u6EtcMGX/oIEds=";
    private static PredictionEngine<PlaceData, PlacePrediction> CreatePredictionEngine(string modelPath)
    {
        var mlContext = new MLContext();
        ITransformer trainedModel = mlContext.Model.Load(modelPath, out var _);
        return mlContext.Model.CreatePredictionEngine<PlaceData, PlacePrediction>(trainedModel);
    }

    public float PredictScore(PlaceData input, string modelPath = "place_model.zip")
    {
        var engine = CreatePredictionEngine(modelPath);
        var prediction = engine.Predict(input);
        return prediction.Score;
    }
    // Định nghĩa lớp PlaceData và PlacePrediction trong DataExporter
    public class PlaceData
    {
        [LoadColumn(1)] // Cột Latitude trong CSV (cột thứ 2, index bắt đầu từ 0)
        public float Latitude { get; set; }

        [LoadColumn(2)]
        public float Longitude { get; set; }

        [LoadColumn(3)]
        public string Category { get; set; }

        [LoadColumn(4)]
        public float Rating { get; set; }

        [LoadColumn(5)]
        public bool Label { get; set; }
    }

    public class PlacePrediction
    {
        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }

        public float Probability { get; set; }
        public float Score { get; set; }
    }
    public class TrainingData
    {
        public int AccountId { get; set; }
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public string Category { get; set; }
        public float Rating { get; set; }
        public bool Label { get; set; }
    }


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
                    Latitude = (float)reader.GetDouble(1),
                    Longitude = (float)reader.GetDouble(2),
                    Category = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                    Rating = reader.IsDBNull(4) ? 0 : (float)reader.GetDouble(4),
                    Label = true
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
                    Latitude = (float)place.lat,
                    Longitude = (float)place.lon,
                    Category = place.category,
                    Rating = place.rating,
                    Label = false
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

    // Hàm huấn luyện mô hình
    public void TrainModel(string csvPath, string modelPath)
    {
        var mlContext = new MLContext();

        // 1. Load dữ liệu
        var data = mlContext.Data.LoadFromTextFile<PlaceData>(
            path: csvPath,
            hasHeader: true,
            separatorChar: ',');
        var labelColumn = mlContext.Data.CreateEnumerable<PlaceData>(data, reuseRowObject: false)
                   .Select(x => x.Label)
                   .ToList();

        Console.WriteLine($"Total samples: {labelColumn.Count}");
        Console.WriteLine($"True labels: {labelColumn.Count(l => l == true)}");
        Console.WriteLine($"False labels: {labelColumn.Count(l => l == false)}");

        // 2. Tách dữ liệu train/test
        var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);

        // 3. Pipeline xử lý dữ liệu và huấn luyện mô hình
        var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Category")
    .Append(mlContext.Transforms.Categorical.OneHotEncoding("CategoryEncoded", "Category")) // one-hot encode category
    .Append(mlContext.Transforms.Concatenate("Features", "Latitude", "Longitude", "Rating", "CategoryEncoded"))
    .Append(mlContext.Transforms.NormalizeMinMax("Features"))
    .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName: "Label", featureColumnName: "Features"));


        // 4. Huấn luyện
        var model = pipeline.Fit(split.TrainSet);

        // 5. Đánh giá mô hình
        var predictions = model.Transform(split.TestSet);
        //var metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        //Console.WriteLine($"✅ Accuracy: {metrics.Accuracy:P2}");
        //Console.WriteLine($"   AUC: {metrics.AreaUnderRocCurve:P2}");
        //Console.WriteLine($"   F1 Score: {metrics.F1Score:P2}");

        // 6. Lưu mô hình
        mlContext.Model.Save(model, split.TrainSet.Schema, modelPath);
        Console.WriteLine($"✅ Model saved to {modelPath}");
    }

}
