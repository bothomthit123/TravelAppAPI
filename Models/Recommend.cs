namespace TravelApp.Models
{
    public class Recommend
    {
    }
    public class RecommendationRequest
    {
        public int AccountId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
    public class RecommendedPlaceDto
    {
        public int PlaceId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Category { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Score { get; set; } // Điểm gợi ý
    }

}
