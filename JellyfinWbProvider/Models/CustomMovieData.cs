using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace JellyfinWbProvider.Models
{
    public class CustomMovieData
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("comment1")]
        public string? Summary { get; set; }

        [JsonPropertyName("create_time")]
        public string? CreateTime { get; set; }

        [JsonPropertyName("tag")]
        public string? Tags { get; set; }

        [JsonPropertyName("comment2")]
        public string? PosterUrl { get; set; }

        [JsonPropertyName("artist")]
        public string? Artist { get; set; }

        [JsonPropertyName("writer")]
        public string? Studio { get; set; }

        [JsonPropertyName("score")]
        public float? Score { get; set; }

        // CreateTimeから年を取得するプロパティ
        public int Year 
        { 
            get 
            {
                if (DateTime.TryParseExact(CreateTime, "yyyy-MM-dd HH:mm:ss", 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, 
                    out DateTime date))
                {
                    return date.Year;
                }
                return 0;
            }
        }

        // タグから評価を計算するメソッド
        public float? GetCommunityRating()
        {
            if (string.IsNullOrEmpty(Tags))
                return null;

            // ★の数を数える
            int starCount = Tags.Count(c => c == '★');
            
            return starCount switch
            {
                5 => 10.0f,
                4 => 8.0f,
                3 => 6.0f,
                2 => 4.0f,
                1 => 2.0f,
                _ => null
            };
        }
    }
}