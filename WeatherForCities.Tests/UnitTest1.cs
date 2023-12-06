

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;

namespace WeatherForCities.Tests
{
    public class WeatherFunctionTests
    {
        private readonly Mock<ILogger<WeatherFunction>> _mockLogger = new Mock<ILogger<WeatherFunction>>();
        private readonly Mock<HttpClient> _mockHttpClient = new Mock<HttpClient>();
        private readonly Mock<Container> _mockContainer = new Mock<Container>();


        [Fact]
        public void Constructor_ThrowsArgumentNullException_IfLoggerIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new WeatherFunction(null));
        }

        // Assuming you have a method to set environment variables for testing
        private void SetEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("CosmosEndpoint", "");
            Environment.SetEnvironmentVariable("CosmosKey", "");
            Environment.SetEnvironmentVariable("CosmosDatabaseName", "");
            Environment.SetEnvironmentVariable("CosmosContainerName", "");

            Environment.SetEnvironmentVariable("WeatherAPIBase", "");
            Environment.SetEnvironmentVariable("WeatherAPIKey", "");
            // Set other required environment variables
        }

        [Fact]
        public void InitializeCosmosClient_ThrowsArgumentNullException_IfCosmosEndpointIsMissing()
        {
            // Arrange
            SetEnvironmentVariables();
            Environment.SetEnvironmentVariable("CosmosEndpoint", null);
            var function = new WeatherFunction(_mockLogger.Object, _mockContainer.Object, _mockHttpClient.Object); // Mock dependencies

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => function.InitializeCosmosClient());
        }


        // Helper method to setup a mocked WeatherFunction
        private WeatherFunction SetupMockWeatherFunction()
        {
            // Mock ILogger
            var mockLogger = new Mock<ILogger<WeatherFunction>>();

            // Mock HttpClient - you will need to setup the expected responses as well
            var mockHttpClient = new Mock<HttpClient>();

            // Mock Cosmos Container
            var mockContainer = new Mock<Container>();

            SetEnvironmentVariables();

            // Create an instance of WeatherFunction using the mocked dependencies
            var weatherFunction = new WeatherFunction(mockLogger.Object, mockContainer.Object, mockHttpClient.Object);

            return weatherFunction;
        }

        [Fact]
        public async Task ProcessCityWeather_LogsErrorForUnsuccessfulApiResponse()
        {
            // Arrange
            var mockFunction = SetupMockWeatherFunction();

            // Act
            await mockFunction.ProcessCityWeather("TestCity");

            // Assert
            _mockLogger.Verify(log => log.LogError(It.IsAny<string>()), Times.Once);
        }

        [Theory]
        [InlineData(1063, true)] // Rain code
        [InlineData(1000, false)] // Non-rain code
        public void IsRainy_ReturnsExpectedResult(int conditionCode, bool expectedResult)
        {
            // Arrange
            var function = SetupMockWeatherFunction();

            // Act
            var result = function.IsRainy(conditionCode);

            // Assert
            Assert.Equal(expectedResult, result);
        }
    }
}