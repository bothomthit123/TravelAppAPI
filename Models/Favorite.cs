using SmartTravelAPI.Models;

public class Favorite
{
    public int FavoriteId { get; set; }
    public int AccountId { get; set; }

    // Thông tin địa điểm (lưu trực tiếp vào Favorite)
    public string Name { get; set; }
    public string Address { get; set; }
    public string Category { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Description { get; set; }
    public double? Rating { get; set; }
    public DateTime SavedAt { get; set; }
}
