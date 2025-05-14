using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartTravelAPI.Models
{
    public class SearchHistory
    {
        [Key]
        public int SearchId { get; set; }

        public int AccountId { get; set; }

        public string SearchQuery { get; set; }

        public DateTime SearchedAt { get; set; }
        [JsonIgnore]
        public Account Account { get; set; }
    }
}
