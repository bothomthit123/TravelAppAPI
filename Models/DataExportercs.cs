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
    private readonly string _connectionString = "Data Source=SQL1003.site4now.net;Initial Catalog=db_ab8497_travelapp;User Id=db_ab8497_travelapp_admin;Password=Nhuquynh257";
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
        [LoadColumn(0)]
        public int AccountId { get; set; }

        [LoadColumn(1)]
        public float Latitude { get; set; }

        [LoadColumn(2)]
        public float Longitude { get; set; }

        [LoadColumn(3)]
        public string Category { get; set; }

        [LoadColumn(4)]
        public float Rating { get; set; }

        [LoadColumn(5)]
        public float IsFavorite { get; set; }

        [LoadColumn(6)]
        public float SearchMatch { get; set; }

        [LoadColumn(7)]
        public float CategoryMatch { get; set; }

        [LoadColumn(8)]
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
        public float IsFavorite { get; set; }        // 1 nếu user yêu thích
        public float SearchMatch { get; set; }       // 1 nếu tên địa điểm trùng với lịch sử tìm kiếm
        public float CategoryMatch { get; set; }     // 1 nếu category thuộc danh sách yêu thích

    }


    public async Task ExportTrainingDataAsync(string outputCsvPath)
    {
        var allData = new List<PlaceData>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Lấy tất cả AccountId có dữ liệu
        var accounts = new List<int>();
        var cmdAcc = new SqlCommand("SELECT DISTINCT AccountId FROM Favorite", conn);
        using var readerAcc = await cmdAcc.ExecuteReaderAsync();
        while (await readerAcc.ReadAsync())
        {
            accounts.Add(readerAcc.GetInt32(0));
        }
        readerAcc.Close();

        foreach (var accId in accounts)
        {
            // Favorite = Label = 1
            var favs = new List<PlaceData>();
            var cmdFav = new SqlCommand("SELECT Latitude, Longitude, Category, Rating FROM Favorite WHERE AccountId = @acc", conn);
            cmdFav.Parameters.AddWithValue("@acc", accId);
            using var readerFav = await cmdFav.ExecuteReaderAsync();
            while (await readerFav.ReadAsync())
            {
                favs.Add(new PlaceData
                {
                    AccountId = accId,
                    Latitude = (float)readerFav.GetDouble(0),
                    Longitude = (float)readerFav.GetDouble(1),
                    Category = readerFav.IsDBNull(2) ? "Unknown" : readerFav.GetString(2),
                    Rating = readerFav.IsDBNull(3) ? 0 : (float)readerFav.GetDouble(3),
                    IsFavorite = 1,
                    SearchMatch = 0,
                    CategoryMatch = 1,
                    Label = true
                });
            }
            readerFav.Close();

            // Từ khóa đã tìm
            var keywords = new List<string>();
            var cmdSearch = new SqlCommand("SELECT DISTINCT SearchQuery FROM SearchHistory WHERE AccountId = @acc", conn);
            cmdSearch.Parameters.AddWithValue("@acc", accId);
            using var readerSearch = await cmdSearch.ExecuteReaderAsync();
            while (await readerSearch.ReadAsync())
            {
                keywords.Add(readerSearch.GetString(0).ToLower());
            }
            readerSearch.Close();

            // Tạo danh sách không yêu thích từ Foursquare
            var notFavs = await GetFoursquarePlacesAsync(favs.First().Latitude, favs.First().Longitude, favs.Select(x => x.Category).ToList());

            foreach (var nf in notFavs)
            {
                var searchMatch = keywords.Any(k => nf.category.ToLower().Contains(k)) ? 1f : 0f;
                var categoryMatch = favs.Any(f => f.Category == nf.category) ? 1f : 0f;

                allData.Add(new PlaceData
                {
                    AccountId = accId,
                    Latitude = (float)nf.lat,
                    Longitude = (float)nf.lon,
                    Category = nf.category,
                    Rating = nf.rating,
                    IsFavorite = 0,
                    SearchMatch = searchMatch,
                    CategoryMatch = categoryMatch,
                    Label = false
                });
            }

            allData.AddRange(favs);
        }

        // Xuất file CSV
        using var writer = new StreamWriter(outputCsvPath);
        using var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(allData);
        Console.WriteLine($"✅ Exported {allData.Count} rows to {outputCsvPath}");
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
    .Append(mlContext.Transforms.Concatenate("Features",
    "Latitude", "Longitude", "Rating", "CategoryEncoded",
    "IsFavorite", "SearchMatch", "CategoryMatch"))
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
