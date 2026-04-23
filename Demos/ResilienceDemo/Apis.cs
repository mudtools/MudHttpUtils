using Mud.HttpUtils.Attributes;
using ResilienceDemo.Models;

namespace ResilienceDemo.Apis;

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IWeatherApi
{
    [Get("/api/weather/forecast")]
    Task<List<WeatherForecast>?> GetForecastAsync([Query] string? city = null);

    [Get("/api/weather/current/{city}")]
    Task<WeatherForecast?> GetCurrentAsync([Path] string city);
}

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IProductApi
{
    [Get("/api/products/{id}")]
    Task<ProductInfo?> GetProductAsync([Path] int id);

    [Post("/api/products")]
    Task<ProductInfo?> CreateProductAsync([Body] ProductInfo product);

    [Get("/api/products")]
    Task<List<ProductInfo>?> SearchProductsAsync([Query] string keyword);
}
