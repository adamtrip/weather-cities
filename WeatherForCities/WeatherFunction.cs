using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace WeatherForCities
{
    public class WeatherFunction
    {
        private readonly ILogger<WeatherFunction> _logger;
        private readonly HttpClient _httpClient = new();
        private readonly string[] _cities = { "Medellín", "Charleston", "London", "Lisbon", "Campinas" };
        private readonly int[] _rainCodes = { 1063, 1180, 1183, 1186, 1189, 1192, 1195, 1198, 1201, 1240, 1243, 1246, 1273, 1276 };
        private readonly string _weatherApiBase;
        private readonly string _weatherApiKey;
        private readonly Container _cosmosContainer;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="logger"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public WeatherFunction(ILogger<WeatherFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _weatherApiBase = Environment.GetEnvironmentVariable("WeatherAPIBase") ?? throw new ArgumentNullException("WeatherAPIBase environment variable not set");
            _weatherApiKey = Environment.GetEnvironmentVariable("WeatherAPIKey") ?? throw new ArgumentNullException("WeatherAPIKey environment variable not set");
            _cosmosContainer = InitializeCosmosClient();
        }

        public WeatherFunction(ILogger<WeatherFunction> logger, Container cosmosContainer, HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _weatherApiBase = Environment.GetEnvironmentVariable("WeatherAPIBase") ?? throw new ArgumentNullException("WeatherAPIBase environment variable not set");
            _weatherApiKey = Environment.GetEnvironmentVariable("WeatherAPIKey") ?? throw new ArgumentNullException("WeatherAPIKey environment variable not set");
            _cosmosContainer = cosmosContainer;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Initializes CosmosDB client and throws an exception if no params are defined
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public Container InitializeCosmosClient()
        {
            var cosmosEndpoint = Environment.GetEnvironmentVariable("CosmosEndpoint") ?? throw new ArgumentNullException("CosmosEndpoint environment variable not set");
            var cosmosKey = Environment.GetEnvironmentVariable("CosmosKey") ?? throw new ArgumentNullException("CosmosKey environment variable not set");
            var databaseName = Environment.GetEnvironmentVariable("CosmosDatabaseName") ?? throw new ArgumentNullException("CosmosDatabaseName environment variable not set");
            var containerName = Environment.GetEnvironmentVariable("CosmosContainerName") ?? throw new ArgumentNullException("CosmosContainerName environment variable not set");

            var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
            return cosmosClient.GetContainer(databaseName, containerName);
        }

        [Function("WeatherFunction")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("Getting weather for offices");

            // I'm using Task.WhenAll because calling multiple await calls usualy bottlenecks the performance
            // This could also could be done with Paralel.ForEach but it depends on the use case
            var tasks = _cities.Select(city => ProcessCityWeather(city)).ToArray();
            await Task.WhenAll(tasks);

            _logger.LogInformation("Weather was processed to all locations");

            // Give a response
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            response.WriteString("Weather data processing completed");

            return response;
        }

        /// <summary>
        /// Processes the weather: fetches data from the WeatherAPI and stores it in CosmosDB
        /// </summary>
        /// <param name="city">City name</param>
        /// <returns></returns>
        public async Task ProcessCityWeather(string city)
        {
            try
            {
                // Get the data from the Weather API 
                var apiResponse = await _httpClient.GetAsync($"{_weatherApiBase}/current.json?key={_weatherApiKey}&q={city}&aqi=no");
                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError($"Error fetching data for {city}: {apiResponse.StatusCode}");
                    return;
                }

                _logger.LogInformation($"Got weather for {city} from WeatherAPI");

                //Process the data from the JSON string into an object
                string data = await apiResponse.Content.ReadAsStringAsync();
                var weatherData = JsonConvert.DeserializeObject<WeatherData>(data, new JsonSerializerSettings()
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                });

                // Add props for the CosmosDB key and partition key
                // It's a bit "hardcoded" and not following naming pattern: should be Id and City
                weatherData.id = Guid.NewGuid();
                weatherData.city = city;

                //Create an item on CosmosDB
                await _cosmosContainer.CreateItemAsync(weatherData, new PartitionKey(city));
                _logger.LogInformation($"Stored {city} data into Cosmos DB");

                //Check for rain
                if (IsRainy(weatherData.Current.Condition.Code))
                {
                    _logger.LogInformation($"City {city} has rain");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception during API call for {city}: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the condition code indicates a rain condition
        /// </summary>
        /// <param name="conditionCode">Condition code from the Weather API</param>
        /// <returns>Returns 'true' or 'false' depending on the condition</returns>
        public bool IsRainy(int conditionCode)
        {
            return _rainCodes.Contains(conditionCode);
        }

        /// <summary>
        /// Weather data base class based on the API Response
        /// </summary>
        public class WeatherData
        {
            public Guid id { get; set; }
            public string city { get; set; } = default!;
            public Location Location { get; set; } = default!;
            public Current Current { get; set; } = default!;
        }

        /// <summary>
        /// Location class where includes extra location information
        /// </summary>
        public class Location
        {
            public string Name { get; set; } = default!;
            public string Region { get; set; } = default!;
            public string Country { get; set; } = default!;
            public double Lat { get; set; }
            public double Lon { get; set; }
            public string Tz_Id { get; set; } = default!;
            public long Localtime_Epoch { get; set; }
            public string Localtime { get; set; } = default!;
        }

        /// <summary>
        /// Class that represents the weather and it's props
        /// </summary>
        public class Current
        {
            public long Last_Updated_Epoch { get; set; }
            public string Last_Updated { get; set; } = default!;
            public double Temp_C { get; set; }
            public double Temp_F { get; set; }
            public int Is_Day { get; set; }
            public Condition Condition { get; set; } = default!;
            public double Wind_Mph { get; set; }
            public double Wind_Kph { get; set; }
            public int Wind_Degree { get; set; }
            public string Wind_Dir { get; set; } = default!;
            public double Pressure_Mb { get; set; }
            public double Pressure_In { get; set; }
            public double Precip_Mm { get; set; }
            public double Precip_In { get; set; }
            public int Humidity { get; set; }
            public int Cloud { get; set; }
            public double Feelslike_C { get; set; }
            public double Feelslike_F { get; set; }
            public double Vis_Km { get; set; }
            public double Vis_Miles { get; set; }
            public double Uv { get; set; }
            public double Gust_Mph { get; set; }
            public double Gust_Kph { get; set; }
        }

        /// <summary>
        /// Class that represents the current condition
        /// </summary>
        public class Condition
        {
            public string Text { get; set; } = default!;
            public string Icon { get; set; } = default!;
            public int Code { get; set; }
        }
    }
}
