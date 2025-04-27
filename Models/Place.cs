using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SmartTravelAPI.Models
{
    public class Place
    {
        public int PlaceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public float? Latitude { get; set; }
        public float? Longitude { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public float? Rating { get; set; }
        public DateTime CreatedAt { get; set; }

        
    }
}
