using Microsoft.AspNetCore.Mvc;
using TravelApp.Models;
using System.Collections.Generic;
using System.Linq;
using System;

[Route("api/[controller]")]
[ApiController]
public class RecommendationController : ControllerBase
{
    private readonly DataContext _context;

    public RecommendationController(DataContext context)
    {
        _context = context;
    }

    [HttpPost("suggest")]
    public ActionResult<IEnumerable<RecommendedPlaceDto>> RecommendPlaces([FromBody] RecommendationRequest request)
    {
        if (request == null || request.AccountId <= 0)
            return BadRequest("Invalid request data.");

        // Lấy các từ khóa tìm kiếm gần đây
        var searchKeywords = _context.SearchHistory
            .Where(s => s.AccountId == request.AccountId)
            .OrderByDescending(s => s.SearchedAt)
            .Take(10)
            .Select(s => s.SearchQuery.ToLowerInvariant())
            .ToList();

        // Lấy các danh mục yêu thích
        var favoriteCategories = _context.Favorite
            .Where(f => f.AccountId == request.AccountId && !string.IsNullOrEmpty(f.Category))
            .GroupBy(f => f.Category.ToLowerInvariant())
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Select(g => g.Category)
            .ToList();

        // Gợi ý địa điểm
        var recommendedPlaces = _context.Places
            .Where(p => p.Latitude.HasValue && p.Longitude.HasValue)
            .AsEnumerable() // Chuyển sang xử lý trong bộ nhớ
            .Select(p => new
            {
                p.PlaceId,
                p.Name,
                p.Address,
                p.Category,
                Latitude = p.Latitude.Value,
                Longitude = p.Longitude.Value,
                Distance = GetDistance(
                    request.Latitude,
                    request.Longitude,
                    (double)p.Latitude.Value,
                    (double)p.Longitude.Value
                ),
                KeywordScore = searchKeywords.Any(k => !string.IsNullOrEmpty(p.Name) && p.Name.ToLowerInvariant().Contains(k)) ? 1 : 0,
                CategoryScore = !string.IsNullOrEmpty(p.Category) && favoriteCategories.Contains(p.Category.ToLowerInvariant()) ? 1 : 0
            })
            .OrderBy(p => p.Distance)
            .ThenByDescending(p => p.KeywordScore + p.CategoryScore)
            .Take(10)
            .Select(p => new RecommendedPlaceDto
            {
                PlaceId = p.PlaceId,
                Name = p.Name,
                Address = p.Address,
                Category = p.Category,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Score = Math.Round(1.0 / (1.0 + p.Distance) + p.KeywordScore + p.CategoryScore, 2)
            })
            .ToList();

        return Ok(recommendedPlaces);
    }

    // Hàm tính khoảng cách Haversine (km)
    private double GetDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Bán kính trái đất (km)
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
