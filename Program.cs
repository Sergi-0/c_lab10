using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Stock
{
    [Key]
    public int Id { get; set; }
    public string Ticker { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public DateTime Date { get; set; }
}

public class TodaysCondition
{
    [Key]
    public int Id { get; set; }
    public string Ticker { get; set; }
    public decimal PreviousPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    [Column("PriceChangeStatus")]
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

    [JsonProperty("t")]
    public long[] Timestamps { get; set; }
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

    static async Task Main(string[] args)
    {
        using (var context = new StockContext())
        {
            await context.Database.MigrateAsync();

            string[] tickers = File.ReadAllLines("ticker.txt");
            var toDate = DateTime.Now;
            var fromDate = toDate.AddYears(-1);

            foreach (var ticker in tickers)
            {
                await ProcessTickerAsync(context, ticker, fromDate, toDate);
                await Task.Delay(1000);
            }

            await AnalyzeStockPricesAsync(context);

            while (true)
            {
                Console.Write("Введите тикер акции (или 'exit' для выхода): ");
                string ticker = Console.ReadLine().ToUpper();

                if (ticker == "EXIT") break;

                string condition = await GetStockConditionAsync(context, ticker);
                Console.WriteLine(condition);
            }
        }
    }

    static async Task ProcessTickerAsync(StockContext context, string ticker, DateTime fromDate, DateTime toDate)
    {
        string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/?from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {API_KEY}");
        
        var response = await client.SendAsync(request);
        var jsonResponse = await response.Content.ReadAsStringAsync();
        var data = JsonConvert.DeserializeObject<StockDataResponse>(jsonResponse);

        for (int i = 0; i < data.High.Length; i++)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(data.Timestamps[i]).DateTime;
            var stock = new Stock
            {
                Ticker = ticker,
                HighPrice = (decimal)data.High[i],
                LowPrice = (decimal)data.Low[i],
                Date = date
            };

            context.Stocks.Add(stock);
        }

        await context.SaveChangesAsync();
        Console.WriteLine($"Обработан тикер {ticker}: сохранено {data.High.Length} записей");
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

            var previousStock = stockPrices[stockPrices.Count - 2];
            var currentStock = stockPrices[stockPrices.Count - 1];

            var previousPrice = (previousStock.HighPrice + previousStock.LowPrice) / 2;
            var currentPrice = (currentStock.HighPrice + currentStock.LowPrice) / 2;

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

        await context.SaveChangesAsync();
    }

    static async Task<string> GetStockConditionAsync(StockContext context, string ticker)
    {
        var condition = await context.TodaysConditions
            .Where(c => c.Ticker == ticker)
            .OrderByDescending(c => c.Date)
            .FirstOrDefaultAsync();

        return condition != null 
            ? $"Акция {ticker}: {condition.PriceChange} " +
              $"(Текущая цена: {condition.CurrentPrice:F2}, " +
              $"Предыдущая цена: {condition.PreviousPrice:F2})" 
            : "Данные для акции не найдены";
    }
}
