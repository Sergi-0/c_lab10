using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

// Модели данных
public class Stock
{
    [Key]
    public int Id { get; set; }
    public string Ticker { get; set; }
    public decimal Price { get; set; }
    public DateTime Date { get; set; }
}

public class TodaysCondition
{
    [Key]
    public int Id { get; set; }
    public string Ticker { get; set; }
    public decimal PreviousPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string PriceChange { get; set; }
    public DateTime Date { get; set; }
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

public class StockContext : DbContext
{
    public DbSet<Stock> Stocks { get; set; }
    public DbSet<TodaysCondition> TodaysConditions { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(@"Server=localhost,1433;Database=StockPriceDB;User Id=SA;Password=YourStrong!Passw0rd;TrustServerCertificate=True;");
    }
}

class Program
{
    private static readonly string API_KEY = "OGdjYU5YZElRQkg0QVNOZDRiRUN4YWtOb2JNNkZReGMzd1BBeDBLc1pEST0";
    private static readonly HttpClient client = new HttpClient();

    static void Main(string[] args)
    {
        MainAsync().GetAwaiter().GetResult();
    }

    static async Task MainAsync()
    {
        try
        {
            using (var context = new StockContext())
            {
                // Применение миграций при запуске
                await context.Database.MigrateAsync();

                string[] tickers = File.ReadAllLines("ticker.txt");
                var fromDate = "2023-01-23";
                var toDate = "2023-12-23";

                foreach (var ticker in tickers)
                {
                    if (tickers.First() != ticker)
                    {
                        await Task.Delay(1000);
                    }
                    await ProcessTickerAsync(context, ticker, fromDate, toDate);
                }

                // Анализ изменения цен
                await AnalyzeStockPricesAsync(context);

                // Интерактивный режим запроса состояния акции
                while (true)
                {
                    Console.Write("Введите тикер акции (или 'exit' для выхода): ");
                    string ticker = Console.ReadLine().ToUpper();

                    if (ticker == "EXIT")
                        break;

                    string condition = await GetStockConditionAsync(context, ticker);
                    Console.WriteLine(condition);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
    }

    static async Task ProcessTickerAsync(StockContext context, string ticker, string fromDate, string toDate)
    {
        try
        {
            string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {API_KEY}");
            var response = await client.SendAsync(request);

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<StockDataResponse>(jsonResponse);

            if (data?.Status == "ok" && data.High != null && data.Low != null && data.High.Length > 0)
            {
                for (int i = 0; i < data.High.Length; i++)
                {
                    var averagePrice = (data.High[i] + data.Low[i]) / 2;
                    var stock = new Stock
                    {
                        Ticker = ticker,
                        Price = (decimal)averagePrice,
                        Date = DateTime.Parse(fromDate).AddDays(i)
                    };

                    context.Stocks.Add(stock);
                }

                await context.SaveChangesAsync();
                Console.WriteLine($"Обработан тикер {ticker}: сохранено {data.High.Length} записей");
            }
            else
            {
                Console.WriteLine($"Ошибка при обработке {ticker}: {data?.Status ?? "неизвестная ошибка"}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке {ticker}: {ex.Message}");
        }
    }

    static async Task AnalyzeStockPricesAsync(StockContext context)
    {
        var tickers = await context.Stocks.Select(s => s.Ticker).Distinct().ToListAsync();

        foreach (var ticker in tickers)
        {
            var stockPrices = await context.Stocks
                .Where(s => s.Ticker == ticker)
                .OrderBy(s => s.Date)
                .ToListAsync();

            if (stockPrices.Count >= 2)
            {
                var previousPrice = stockPrices[stockPrices.Count - 2].Price;
                var currentPrice = stockPrices[stockPrices.Count - 1].Price;

                var condition = new TodaysCondition
                {
                    Ticker = ticker,
                    PreviousPrice = previousPrice,
                    CurrentPrice = currentPrice,
                    PriceChange = currentPrice > previousPrice ? "Выросла" : "Упала",
                    Date = DateTime.Now
                };

                context.TodaysConditions.Add(condition);
            }
        }

        await context.SaveChangesAsync();
    }

    static async Task<string> GetStockConditionAsync(StockContext context, string ticker)
    {
        var condition = await context.TodaysConditions
            .Where(c => c.Ticker == ticker)
            .OrderByDescending(c => c.Date)
            .FirstOrDefaultAsync();

        return condition != null 
            ? $"Акция {ticker}: {condition.PriceChange} (Текущая цена: {condition.CurrentPrice}, Предыдущая цена: {condition.PreviousPrice})" 
            : "Данные для акции не найдены";
    }
}
