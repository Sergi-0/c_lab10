using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace StockDataAnalyzer
{
    public class StockDatabaseContext : DbContext
    {
        public DbSet<StockTicker> StockTickers => Set<StockTicker>();
        public DbSet<StockPrice> StockPrices => Set<StockPrice>();
        public DbSet<StockDailyCondition> StockDailyConditions => Set<StockDailyCondition>();
        
        public StockDatabaseContext() => Database.EnsureCreated();
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=stockDatabase.db");
        }
    }

    public class StockPrice
    {
        public int Id { get; set; }
        public int? TickerId { get; set; }
        public double PriceValue { get; set; }
        public string? RecordDate { get; set; }
        public string? TickerSymbol { get; set; }
    }

    public class StockTicker
    {
        public int Id { get; set; }
        public string? TickerSymbol { get; set; }
    }

    public class StockDailyCondition
    {
        public int Id { get; set; }
        public int? TickerId { get; set; }
        public double? PriceChange { get; set; }
        public string? TickerSymbol { get; set; }
    }

    class StockDataProcessor
    {
        static readonly Mutex ProcessingMutex = new();

        static async Task Main()
        {
            List<string> tickerSymbols = [];

            using (StreamReader reader = new("ticker.txt"))
            {
                string currentTicker;
                while ((currentTicker = reader.ReadLine()) != null)
                {
                    tickerSymbols.Add(currentTicker);
                }
            }

            List<Task> processingTasks = [];
            int tickerIndex = 1;
            foreach (string tickerSymbol in tickerSymbols)
            {
                processingTasks.Add(ProcessStockData(tickerSymbol, tickerIndex));
                System.Threading.Thread.Sleep(500);
                tickerIndex++;
            }

            await Task.WhenAll(processingTasks);

            Console.WriteLine("Stock prices have been saved to the database");
            Console.WriteLine("Stock tickers have been processed");

            Console.WriteLine("Select a ticker symbol");
            string? selectedTicker = Console.ReadLine();

            using (StockDatabaseContext database = new())
            {
                var selectedTickerRecord = database.StockTickers.FirstOrDefault(t => t.TickerSymbol == selectedTicker);
                var dailyCondition = database.StockDailyConditions.FirstOrDefault(t => t.Id == selectedTickerRecord.Id);
                Console.WriteLine(dailyCondition.PriceChange);
            }
        }

        static async Task ProcessStockData(string tickerSymbol, int tickerIndex)
        {
            try
            {
                using HttpClient httpClient = new();

                string apiUrl = $"https://api.marketdata.app/v1/stocks/candles/D/{tickerSymbol}/?from=2023-11-16&to=2024-11-15&token=UmF5LW9nUDJoam9NdURSMFM0WXJpMm5JcDI1RWlsRWJUb1drRi03QzVOcz0";

                HttpResponseMessage apiResponse = await httpClient.GetAsync(apiUrl);
                string responseContent = await apiResponse.Content.ReadAsStringAsync();
                dynamic responseData = Newtonsoft.Json.JsonConvert.DeserializeObject(responseContent);
                
                double currentAveragePrice = 0;
                double priceChangeAmount = 0;
                
                if (responseData != null && responseData.t != null && responseData.h != null && responseData.l != null)
                {
                    List<long> timestamps = responseData?.t?.ToObject<List<long>>() ?? new List<long>();
                    List<double> highPrices = responseData?.h?.ToObject<List<double>>() ?? new List<double>();
                    List<double> lowPrices = responseData?.l?.ToObject<List<double>>() ?? new List<double>();
                    
                    if (timestamps.Count >= 2)
                    {
                        long latestTimestamp = timestamps[timestamps.Count - 1];
                        DateTime baseDate = new DateTime(1970, 1, 1);
                        DateTime recordDate = baseDate.AddSeconds(latestTimestamp);
                        string formattedDate = recordDate.ToString();

                        currentAveragePrice = (highPrices[timestamps.Count - 1] + lowPrices[timestamps.Count - 1]) / 2;
                        Console.WriteLine($"{tickerSymbol}:{formattedDate}, {currentAveragePrice}");

                        priceChangeAmount = currentAveragePrice - (highPrices[timestamps.Count - 2] + lowPrices[timestamps.Count - 2]) / 2;
                        
                        SaveStockData(tickerSymbol, tickerIndex, currentAveragePrice, priceChangeAmount, formattedDate);
                    }
                    else
                    {
                        SaveStockData(tickerSymbol, tickerIndex, 0, 0, "0");
                        Console.WriteLine($"{tickerSymbol}:0, {currentAveragePrice}");
                    }
                }
                else
                {
                    SaveStockData(tickerSymbol, tickerIndex, 0, 0, "0");
                    Console.WriteLine($"{tickerSymbol}:0, {currentAveragePrice}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {tickerSymbol}: {ex.Message}");
            }
        }

        static void SaveStockData(string tickerSymbol, int tickerIndex, double averagePrice, double priceChange, string recordDate)
        {
            using (StockDatabaseContext database = new())
            {
                var newTicker = new StockTicker { TickerSymbol = tickerSymbol };
                database.StockTickers.Add(newTicker);
                database.SaveChanges();

                var dailyCondition = new StockDailyCondition
                {
                    TickerId = tickerIndex,
                    PriceChange = priceChange,
                    TickerSymbol = tickerSymbol
                };
                database.StockDailyConditions.Add(dailyCondition);
                database.SaveChanges();

                var stockPrice = new StockPrice
                {
                    TickerId = tickerIndex,
                    PriceValue = averagePrice,
                    RecordDate = recordDate,
                    TickerSymbol = tickerSymbol
                };

                database.StockPrices.Add(stockPrice);
                database.SaveChanges();
            }
        }
    }
}
