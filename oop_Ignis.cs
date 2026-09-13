using System;
using System.Collections.Generic;

namespace wowo
{
    public enum Role
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

    public enum HotspotSource
    {
        NasaFirms,
        Manual
    }

    // Simple 2D vector for wind speed/direction representation.
    public class Vector
    {
        public double Magnitude { get; private set; }
        public double Direction { get; private set; }

        public Vector(double magnitude, double direction)
        {
            Magnitude = magnitude;
            Direction = direction;
        }
    }

    public class AuthenticationService
    {
        public User Authenticate(string email, string password)
        {
            // logika: cari user berdasarkan email lalu cocokkan password hash
            return null;
        }

        public User Register(string name, string email, string password)
        {
            // logika: buat user baru, hash password, simpan ke storage
            return null;
        }

        public void Logout()
        {
            // logika: invalidate sesi user yang sedang login
        }
    }

    public class User
    {
        private int userId;
        private string name;
        private string email;
        private string passwordHash;
        private Role role;
        private DateTime createdAt;

        public bool Login()
        {
            // logika: validasi kredensial lalu buat sesi
            return false;
        }

        public void Logout()
        {
            // logika: hapus sesi aktif
        }
    }

    public class Admin : User
    {
        public Hotspot AddManualHotspot()
        {
            // logika: buat ManualHotspot baru dari input admin lalu simpan
            return null;
        }

        public void UpdateHotspot()
        {
            // logika: update data hotspot yang sudah ada
        }

        public void DeleteHotspot()
        {
            // logika: hapus hotspot dari storage
        }
    }

    public class RegisteredUser : User
    {
        private List<MonitoringArea> monitoringAreas = new List<MonitoringArea>();

        public void AddMonitoringArea(MonitoringArea area)
        {
            monitoringAreas.Add(area);
        }

        public void RemoveMonitoringArea(MonitoringArea area)
        {
            monitoringAreas.Remove(area);
        }
    }

    public class Notification
    {
        public string NotificationId { get; private set; }
        public string Title { get; private set; }
        public string Message { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public bool IsRead { get; private set; }
        public string HotspotId { get; private set; }

        public Notification(string title, string message, string hotspotId)
        {
            NotificationId = Guid.NewGuid().ToString();
            Title = title;
            Message = message;
            HotspotId = hotspotId;
            CreatedAt = DateTime.UtcNow;
            IsRead = false;
        }

        public void Send()
        {
            // logika: kirim notifikasi ke user (push/email/dsb)
        }

        public void MarkAsRead()
        {
            IsRead = true;
        }
    }

    public class NotificationService
    {
        public List<Hotspot> CheckNewHotspots()
        {
            // logika: bandingkan hotspot terbaru dengan yang sudah diketahui
            return new List<Hotspot>();
        }

        public void SendNotification(Notification notification)
        {
            notification.Send();
        }
    }

    public abstract class Hotspot
    {
        public string HotspotId { get; protected set; }
        public double Latitude { get; protected set; }
        public double Longitude { get; protected set; }
        public DateTime DetectedAt { get; protected set; }
        public double Confidence { get; protected set; }
        public double Brightness { get; protected set; }
        public HotspotSource Source { get; protected set; }
        public string Status { get; protected set; }

        protected Hotspot(double latitude, double longitude, HotspotSource source)
        {
            HotspotId = Guid.NewGuid().ToString();
            Latitude = latitude;
            Longitude = longitude;
            Source = source;
            DetectedAt = DateTime.UtcNow;
            Status = "Active";
        }

        public (double Latitude, double Longitude) GetLocation()
        {
            return (Latitude, Longitude);
        }

        public string GetDetails()
        {
            return $"[{HotspotId}] {Source} at ({Latitude}, {Longitude}), status: {Status}";
        }
    }

    public class SatelliteHotspot : Hotspot
    {
        public string SatelliteName { get; private set; }
        public string Instrument { get; private set; }
        public DateTime ScanTime { get; private set; }

        public SatelliteHotspot(double latitude, double longitude, string satelliteName, string instrument, DateTime scanTime)
            : base(latitude, longitude, HotspotSource.NasaFirms)
        {
            SatelliteName = satelliteName;
            Instrument = instrument;
            ScanTime = scanTime;
        }
    }

    public class ManualHotspot : Hotspot
    {
        public int ReportedBy { get; private set; }
        public string ReportNote { get; private set; }
        public DateTime IncidentTime { get; private set; }

        public ManualHotspot(double latitude, double longitude, int reportedBy, string reportNote, DateTime incidentTime)
            : base(latitude, longitude, HotspotSource.Manual)
        {
            ReportedBy = reportedBy;
            ReportNote = reportNote;
            IncidentTime = incidentTime;
        }
    }

    public class MonitoringArea
    {
        public string AreaId { get; private set; }
        public string Name { get; private set; }
        public double MinLatitude { get; private set; }
        public double MaxLatitude { get; private set; }
        public double MinLongitude { get; private set; }
        public double MaxLongitude { get; private set; }
        public double Radius { get; private set; }

        public MonitoringArea(string name, double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, double radius)
        {
            AreaId = Guid.NewGuid().ToString();
            Name = name;
            MinLatitude = minLatitude;
            MaxLatitude = maxLatitude;
            MinLongitude = minLongitude;
            MaxLongitude = maxLongitude;
            Radius = radius;
        }

        public bool Contains(double latitude, double longitude)
        {
            return latitude >= MinLatitude && latitude <= MaxLatitude
                && longitude >= MinLongitude && longitude <= MaxLongitude;
        }
    }

    public class WeatherData
    {
        public string WeatherDataId { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public DateTime Timestamp { get; private set; }
        public double Temperature { get; private set; }
        public double Humidity { get; private set; }
        public double WindSpeed { get; private set; }
        public double WindDirection { get; private set; }
        public double Pressure { get; private set; }
        public double Precipitation { get; private set; }
        public string WeatherCondition { get; private set; }

        public WeatherData(string weatherDataId, double latitude, double longitude, double windSpeed, double windDirection, double humidity, DateTime timestamp, double temperature, double pressure, double precipitation, string weatherCondition)
        {
            WeatherDataId = weatherDataId;
            Latitude = latitude;
            Longitude = longitude;
            WindSpeed = windSpeed;
            WindDirection = windDirection;
            Humidity = humidity;
            Timestamp = timestamp;
            Temperature = temperature;
            Pressure = pressure;
            Precipitation = precipitation;
            WeatherCondition = weatherCondition;
        }

        public Vector GetWindVector()
        {
            return new Vector(WindSpeed, WindDirection);
        }
    }

    public abstract class ServiceBase
    {
        protected string ServiceName { get; }

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
            return $"{modelName} v{modelVersion}";
        }

        public void Preprocess()
        {
            // logika: normalisasi/transform input hotspot & weather sebelum prediksi
        }

        public Prediction Predict()
        {
            // logika: jalankan model CA untuk menghasilkan prediksi
            return null;
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
            if (usedWeatherData == null) return;

            Vector wind = usedWeatherData.GetWindVector();
            spreadDirection = wind.Direction;
            spreadSpeed = wind.Magnitude;
            estimatedArea = Math.PI * spreadSpeed * spreadSpeed;
            riskLevel = DetermineRiskLevel(spreadSpeed);
        }

        public RiskLevel GetRiskLevel()
        {
            return riskLevel;
        }

        private RiskLevel DetermineRiskLevel(double speed)
        {
            if (speed >= 15) return RiskLevel.Critical;
            if (speed >= 10) return RiskLevel.High;
            if (speed >= 5) return RiskLevel.Medium;
            return RiskLevel.Low;
        }
    }

    public class PredictionService : ServiceBase
    {
        private readonly PredictionModel model;
        private readonly WeatherService weatherService;

        public PredictionService(PredictionModel model, WeatherService weatherService) : base(nameof(PredictionService))
        {
            this.model = model;
            this.weatherService = weatherService;
        }

        public Prediction Predict(Hotspot hotspot, WeatherData weatherData)
        {
            model.Preprocess();
            Prediction prediction = new Prediction();
            prediction.AssociateWithHotspot(hotspot, weatherData);
            prediction.CalculateSpread();
            LogAction($"Predicted for hotspot {hotspot.HotspotId}");
            return prediction;
        }

        public Prediction Predict(Hotspot hotspot)
        {
            WeatherData weatherData = weatherService.FetchWeatherData(hotspot.Latitude, hotspot.Longitude);
            return Predict(hotspot, weatherData);
        }

        public double CalculateSpreadDirection(Prediction prediction)
        {
            return prediction.SpreadDirection;
        }

        public double CalculateSpreadSpeed(Prediction prediction)
        {
            return prediction.SpreadSpeed;
        }

        public override void LogAction(string action)
        {
            base.LogAction(action);
        }
    }

    public class MapService : ServiceBase
    {
        public MapService() : base(nameof(MapService))
        {
        }

        public void LoadMap()
        {
            // logika: inisialisasi peta (mis. panggil library peta pihak ketiga)
        }

        public void DisplayHotspots()
        {
            // logika: render marker hotspot di peta
        }

        public void DisplayPredictions()
        {
            // logika: render area sebaran prediksi di peta
        }

        public void CenterLocation()
        {
            // logika: pusatkan peta ke lokasi tertentu (mis. lokasi user)
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
            // logika: panggil API cuaca eksternal menggunakan apiKey & endpoint, lalu parse ke WeatherData
            return null;
        }

        public WeatherData GetNearestWeather(double latitude, double longitude)
        {
            // logika: cari data cuaca terdekat dari cache/storage lokal
            return null;
        }
    }

    public class FIRMSService : ServiceBase
    {
        private readonly string apiKey;
        private readonly string endpoint;

        public FIRMSService(string apiKey, string endpoint) : base(nameof(FIRMSService))
        {
            this.apiKey = apiKey;
            this.endpoint = endpoint;
        }

        public List<Hotspot> FetchHotspots()
        {
            // logika: panggil NASA FIRMS API, parse response jadi list SatelliteHotspot
            return new List<Hotspot>();
        }

        public bool ValidateData(SatelliteHotspot hotspot)
        {
            return hotspot != null
                && hotspot.Confidence >= 0
                && hotspot.Latitude >= -90 && hotspot.Latitude <= 90
                && hotspot.Longitude >= -180 && hotspot.Longitude <= 180;
        }
    }
}
