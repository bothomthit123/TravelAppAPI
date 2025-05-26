using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SmartTravelAPI.Models
{
    public class Place
    {
        public int PlaceId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public double Latitude { get; set; }     // sửa từ float -> double
        public double Longitude { get; set; }    // sửa từ float -> double
        public string Category { get; set; }
        public string Description { get; set; }  // bạn đang dùng nó làm imgURL
        public double Rating { get; set; }       // sửa từ float -> double
        public DateTime CreatedAt { get; set; }
    }

}
