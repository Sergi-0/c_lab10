using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Runtime.Remoting.Contexts;

public class Stock
{
    public int Id { get; set; }
    public string Ticker { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public DateTime Date { get; set; }
}

public class StockCondition
{
    public int Id { get; set; }
    public string Ticker { get; set; }
    public decimal PreviousPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Status { get; set; }
    public DateTime AnalysisDate { get; set; }
}

public class StockDbContext : DbContext
{
    public DbSet<Stock> Stocks { get; set; }
    public DbSet<StockCondition> StockConditions { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=StockDB;User Id=SA;Password=StrongPassword123!;TrustServerCertificate=True;");
    }
}

public class StockDataLoader
{
    private static readonly string API_KEY = "OGdjYU5YZElRQkg0QVNOZDRiRUN4YWtOb2JNNkZReGMzd1BBeDBLc1pEST0";
    private static readonly HttpClient client = new HttpClient();

    public async Task<StockDataResponse> GetStockData(string ticker)
    {
        try
        {
            string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {API_KEY}");
            var response = await client.SendAsync(request);

            var jsonResponse = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<StockDataResponse>(jsonResponse);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки данных: {ex.Message}");
            return null;
        }
    }
}

public class StockAnalyzer
{
    public StockCondition AnalyzeStock(string ticker, decimal currentPrice, decimal previousPrice)
    {
        return new StockCondition
        {
            Ticker = ticker,
            PreviousPrice = previousPrice,
            CurrentPrice = currentPrice,
            Status = currentPrice > previousPrice ? "Выросла" :
                     currentPrice < previousPrice ? "Упала" : "Без изменений",
            AnalysisDate = DateTime.Now
        };
    }
}

public class StockDataResponse
{
    [JsonProperty("s")]
    public string Status { get; set; }

    [JsonProperty("h")]
    public double[] High { get; set; }

    [JsonProperty("l")]
    public double[] Low { get; set; }
}

class Program
{
    static async Task Main()
    {
        var loader = new StockDataLoader();
        var analyzer = new StockAnalyzer();
        var context = new StockDbContext();

        Console.WriteLine("Введите тикер акции:");
        string ticker = Console.ReadLine().ToUpper();

        var stockData = await loader.GetStockData(ticker);

        if (stockData?.Status == "ok" && stockData.High?.Length > 0 && stockData.Low?.Length > 0)
        {
            // Сохраняем данные оstock
            var stock = new Stock
            {
                Ticker = ticker,
                HighPrice = (decimal)stockData.High[^1],
                LowPrice = (decimal)stockData.Low[^1],
                Date = DateTime.Now
            };
            context.Stocks.Add(stock);

            // Получаем предыдущую цену
            var previousStock = context.Stocks
                .Where(s => s.Ticker == ticker)
                .OrderByDescending(s => s.Date)
                .Skip(1)
                .FirstOrDefault();

            decimal previousPrice = previousStock?.HighPrice ?? 0;
            decimal currentPrice = stock.HighPrice;

            // Анализируем изменение цены
            var condition = analyzer.AnalyzeStock(ticker, currentPrice, previousPrice);
            context.StockConditions.Add(condition);

            // Сохраняем изменения
            context.SaveChanges();

            // Выводим результат
            Console.WriteLine($"Акция {ticker}: {condition.Status}");
            Console.WriteLine($"Текущая цена: {currentPrice}");
            Console.WriteLine($"Предыдущая цена: {previousPrice}");
        }
        else
        {
            Console.WriteLine("Не удалось получить данные о акции.");
        }
    }
}
