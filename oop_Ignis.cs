using System;
using System.Reflection.Metadata;
using System.Security.Principal;

namespace wowo
{

    enum Role
    {
        User,
        Admin
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High, 
        Critical
    }
    class AuthenticationService
    {
        public User Authenticate(string email, string password)
        {
            return null;
        }
        public User Register(string name, string email, string password)
        {
            return null;
        }
        public void Logout()
        {
        }
    }
    class User
    {
        private int userId;
        private string name;
        private string email;
        private string passwordHash;
        private Role role;
        private DateTime createdAt;

        public bool Login()
        {
            return 0;
        }
        public void Logout()
        {  
        }
    }

    class Admin : User
    {
        public Hotspot AddManualHotspot()
        {
            return null;
        }

        public void UpdateHotspot()
        {
        }

        public void DeleteHotspot()
        {
        }
    }

    class RegisteredUser : User
    {
        public void AddMonitoringArea()
        {
        }

        public void RemoveMonitoringArea()
        {
        }
    }

    class Notification
    {
        private string notificationId;
        private string title;
        private string message;
        private DateTime createdAt;
        private bool isRead;
        private string hotspotId;

        public void Send()
        {
        }
        public void MarkAsRead()
        {
        }
    }

    public abstract class Hotspot
    {
        public string HotspotId { get;  protected set; }
        public string Latitude { get; protected set; }
        public string Longitude { get;  protected set; }
    }

    public class WeatherData
    {
        private string WeatherDataId { get; private set; }
        private double Latitude { get; private set; }
        private double Longitude { get; private set; }
        private double WindSpeed { get; private set; }
        private double WindDirection { get; private set; }
        private double Humidity { get; private set; }
        private double Timestamp { get; private set; }
        private double Temperature { get; private set; }
        private double Pressure { get; private set; }
        private double Precipitation { get; private set; }
        private string WeatherCondition { get; private set; }

        public WeatherData(string weatherDataId, double latitude, double longitude, double windSpeed, double windDirection, double humidity, double timestamp, double temperature, double pressure, double precipitation, string weatherCondition)
        {
            this.WeatherDataId = weatherDataId;
            this.Latitude = latitude;
            this.Longitude = longitude;
            this.WindSpeed = windSpeed;
            this.WindDirection = windDirection;
            this.Humidity = humidity;
            this.Timestamp = timestamp;
            this.Temperature = temperature;
            this.Pressure = pressure;
            this.Precipitation = precipitation;
            this.WeatherCondition = weatherCondition;
        }

        public string WeatherDataId => WeatherDataId;
        public double Latitude => Latitude;
        public double Longitude => Longitude;
        public double WindSpeed => WindSpeed;
        public double WindDirection => WindDirection;
        public double Humidity => Humidity;
        public double Timestamp => Timestamp;
        public double Temperature => Temperature;
        public double Pressure => Pressure;
        public double Precipitation => Precipitation;
        public string WeatherCondition => WeatherCondition;

        public Vector GetWindVector()
        {
            return new Vector(WindSpeed, WindDirection);
        }
    }

    public abstract class ServiceBase
    {
        protected string ServiceName { get;}
        protected ServiceBase(string serviceName)
        {
            ServiceName = serviceName;
        }
        public virtual void LogAction(string action)
        {
            Console.WriteLine($"[{DateTime.Now}] {ServiceName}: {action}");
        }
    }

    public class PredictionModel
    {
        private string modelName;
        private string modelVersion;

        public PredictionModel(string modelName, string modelVersion)
        {
            this.modelName = modelName;
            this.modelVersion = modelVersion;
        }

        private string GetModelInfo()
        {
            
        }

        public void Preprocess()
        {

        }

        private Prediction Predict()
        {
            
        }
    }

    public class Prediction
    {
        private string predictionId;
        private DateTime createdAt;
        private double spreadDirection;
        private double spreadSpeed;
        private double estimatedArea;
        private double estimatedTime;
        private RiskLevel riskLevel;
        private Hotspot basedOnHotspot;
        private WeatherData usedWeatherData;

        public Prediction()
        {
            predictionId = Guid.NewGuid().ToString();
            createdAt = DateTime.UtcNow;
        }

        public string PredictionId => predictionId;
        public double SpreadDirection => spreadDirection;
        public double SpreadSpeed => spreadSpeed;
        public double EstimatedArea => estimatedArea;
        public double EstimatedTime => estimatedTime;

        public void AssociateWithHotspot(Hotspot hotspot, WeatherData weatherData)
        {
            basedOnHotspot = hotspot;
            usedWeatherData = weatherData;
        }

        public void CalculateSpread()
        {
            
        }

        public Risklevel GetRiskLevel()
        {
            
        }

        private RiskLevel DetermineRiskLevel(double speed)
        {
            
        }
    }

    public class PredictionService : ServiceBase
    {
        private readonly PredictionModel model;
        private readonly WeatherDataService weatherService;

        public PredictionService(PredictionModel model, WeatherDataService weatherService) : base(nameof(PredictionService))
        {
            
        }

        public Prediction Predict(Hotspot hotspot, WeatherData weatherData)
        {

        }

        public Prediction Predict(Hotspot hotspot)
        {
            
        }

        public double CalculateSpreadDirection(Prediction prediction)
        {
            
        }

        public double CalculateSpreadSpeed(Prediction prediction)
        {
            
        }

        public override void LogAction(string action)
        {
            
        }
    }

    public class MapService : ServiceBase
    {
        public MapService() : base(nameof(MapService))
        {
            
        }

        public void LoadMap()
        {
            
        }

        public void DisplayHotspots()
        {
            
        }

        public void DisplayPredictions()
        {
            
        }

        public void CenterLocation()
        {
            
        }
    }

    public class WeatherService : ServiceBase
    {
        private readonly string apiKey;
        private readonly string endpoint;

        public WeatherService(string apiKey, string endpoint) : base(nameof(WeatherService))
        {
            this.apiKey = apiKey;
            this.endpoint = endpoint;
        }

        public WeatherData FetchWeatherData(double latitude, double longitude)
        {
            
        }
    }
}