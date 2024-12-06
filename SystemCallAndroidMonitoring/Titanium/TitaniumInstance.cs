using System;
using System.Net;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.Models;
using Titanium.Web.Proxy.EventArguments;
using System.Security.Cryptography.X509Certificates;
using Titanium.Web.Proxy.Network;
using System.Xml;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;
using System.Text.RegularExpressions;
using System.Text;
using System.Web;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Serilog;
using System.Collections.Concurrent;
using System.Linq;

namespace SystemCallAndroidMonitoring.Titanium
{
    public class AllInstances
    {
        public static Dictionary<string, TitaniumInstance> ActiveInstances { get; set; } = new();
    }
    public class TitaniumInstance
    {

        private string ExternalProxyAddress { get; set; } = string.Empty;
        private int ExternalProxyPort { get; set; } = 0;
        private string ExternalProxyUsername { get; set; } = string.Empty;
        private string ExternalProxyPassword { get; set; } = string.Empty;
        private string ExternalProxyProtocol { get; set; } = "HTTP";
        private DateTime LastRequestAt { get; set; }
        private int MaximumActivityDelaySeconds { get; set; } = 300;
        private bool Closed { get; set; } = false;


        public string ProxyEndpoint { get; set; } = string.Empty;
        public string CertificatePath { get; set; }
        public string CertificatePemData { get; set; }


        private readonly TrafficMonitor _trafficMonitor = new();

        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        private ProxyServer? ProxyServer { get; set; }

        private List<DomainsForReplace> DomainsForReplaces { get; set; } = [];
        private List<ChangeBody> ChangeRequests { get; set; } = [];
        private List<ChangeBody> ChangeResponses { get; set; } = [];

        public TitaniumInstance(
            string? externalProxyAddress = null, 
            int externalProxyPort = 0, 
            string? externalProxyUsername = null, 
            string? externalProxyPassword = null, 
            string? externalProxyProtocol = null,
            int maximumActivityDelaySeconds = 0)
        {
            if (!string.IsNullOrEmpty(externalProxyAddress))
                this.ExternalProxyAddress = externalProxyAddress;
            if (externalProxyPort != 0)
                this.ExternalProxyPort = externalProxyPort;
            if (!string.IsNullOrEmpty(externalProxyUsername))
                this.ExternalProxyUsername = externalProxyUsername;
            if (!string.IsNullOrEmpty(externalProxyPassword))
                this.ExternalProxyPassword = externalProxyPassword;
            if (!string.IsNullOrEmpty(externalProxyProtocol))
                this.ExternalProxyProtocol = externalProxyProtocol;
            if (maximumActivityDelaySeconds != 0)
                this.MaximumActivityDelaySeconds = maximumActivityDelaySeconds;



            // Далее код вашего прокси-сервера
            var proxyServer = new ProxyServer();


            // Генерация сертификата
            proxyServer.CertificateManager.CreateRootCertificate();
            proxyServer.EnableConnectionPool = false;
            proxyServer.ConnectionTimeOutSeconds = 10; // Уменьшение времени ожидания
            
            // Получаем сертификат
            X509Certificate2? cert = proxyServer.CertificateManager.RootCertificate;

            // Сохраняем сертификат в формате .crt
            string certPath = "proxy-cert.crt";
            string fullPath = Path.GetFullPath(certPath);
            File.WriteAllBytes(certPath, cert!.Export(X509ContentType.Cert));
            Log.Information($"Сертификат сохранен в: {fullPath}");
            CertificatePath = fullPath;
            string pemCertificate = ConvertToPem(cert);
            this.CertificatePemData = pemCertificate;
            var proxyType = ExternalProxyType.Http;
            switch (this.ExternalProxyProtocol)
            {
                case "HTTP":
                    proxyType = ExternalProxyType.Http;
                    break;
                case "SOCKS4":
                    proxyType = ExternalProxyType.Socks4;
                    break;
                case "SOCKS5":
                    proxyType = ExternalProxyType.Socks5;
                    break;
            }

            // Настройка внешнего прокси
            if (!string.IsNullOrEmpty(this.ExternalProxyAddress) &&
                !string.IsNullOrEmpty(this.ExternalProxyPassword))
            {
                var upstreamProxyEndPoint = new ExternalProxy()
                {
                    BypassLocalhost = true,
                    ProxyType = proxyType,
                    HostName = this.ExternalProxyAddress,
                    Port = this.ExternalProxyPort
                };
                if (!string.IsNullOrEmpty(this.ExternalProxyUsername))
                    upstreamProxyEndPoint.UserName = this.ExternalProxyUsername;
                if (!string.IsNullOrEmpty(this.ExternalProxyPassword))
                    upstreamProxyEndPoint.Password = this.ExternalProxyPassword;

                proxyServer.UpStreamHttpsProxy = upstreamProxyEndPoint;
                proxyServer.UpStreamHttpProxy = upstreamProxyEndPoint;
            }

            var localIp = GetLocalIpAddress();
            var availablePort = GetAvailablePort();
            // Настройка эндпоинта
            var explicitEndPoint = new ExplicitProxyEndPoint(
                IPAddress.Parse(localIp),
                availablePort,
                true
            );

            // Добавление эндпоинта
            proxyServer.AddEndPoint(explicitEndPoint);
            this.ProxyEndpoint = $"{localIp}:{availablePort}";
            // Подписка на события
            proxyServer.BeforeRequest += OnBeforeRequest;
            proxyServer.BeforeResponse += OnBeforeResponse;
            proxyServer.ExceptionFunc =  exception =>
            {
                //Log.Error($"[ProxyServer] Exception: {exception.Message}", exception);
            };
            // Старт сервера
            proxyServer.Start();
            this.ProxyServer = proxyServer;
            Log.Information($"Прокси запущен на {this.ProxyEndpoint}. Нажмите Enter для выхода.");
            this.LastRequestAt = DateTime.UtcNow;
            AllInstances.ActiveInstances[this.ProxyEndpoint] = this;
            _ = DelayKillerTask();
        }

        private async Task DelayKillerTask()
        {
            while (!Closed)
            {
                await Task.Yield();
                if ((DateTime.UtcNow - LastRequestAt).TotalSeconds > this.MaximumActivityDelaySeconds)
                {
                    this.ProxyServer!.Stop();
                    Log.Information($"Инстанс {this.ProxyEndpoint} остановлен по дилею в {this.MaximumActivityDelaySeconds} сек.");
                    this.Closed = true;
                    if (AllInstances.ActiveInstances.ContainsKey(this.ProxyEndpoint))
                        AllInstances.ActiveInstances.Remove(this.ProxyEndpoint);
                    return;
                }
                await Task.Delay(1000);

            }
            
        }
        private string GetLocalIpAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        private int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private string ConvertToPem(X509Certificate2? certificate)
        {
            // Экспорт сертификата в массив байт
            byte[] certBytes = certificate!.Export(X509ContentType.Cert);

            // Преобразование в Base64
            string base64Cert = Convert.ToBase64String(certBytes);

            // Форматирование в PEM
            return $"-----BEGIN CERTIFICATE-----\n{base64Cert}\n-----END CERTIFICATE-----";
        }

        private async Task OnBeforeRequest(object sender, SessionEventArgs e)
        {

            bool isModified = false;
            try
            {
                var startUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url);
                var thisUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url);

                // Находим все matching домены с изменениями
                var matchingDomains = this.ChangeResponses
                    .Where(x => Regex.IsMatch(thisUrl, x.DomainRegex))
                    .ToList();
                matchingDomains.AddRange(this.ChangeRequests
                    .Where(x => Regex.IsMatch(thisUrl, x.DomainRegex))
                    .ToList());

                if (matchingDomains.Any())
                {
                    // Стратегия минимального вмешательства
                    e.HttpClient.Request.Headers.RemoveHeader("If-None-Match"); // Убираем точечно
                    e.HttpClient.Request.Headers.RemoveHeader("Cache-Control"); // Убираем точечно
                    e.HttpClient.Request.Headers.RemoveHeader("Pragma"); // Убираем точечно
                    e.HttpClient.Request.Headers.RemoveHeader("If-Modified-Since");
                    e.HttpClient.Request.Headers.RemoveHeader("ETag");
                    e.HttpClient.Request.Headers.RemoveHeader("Accept");
                    e.HttpClient.Request.Headers.RemoveHeader("X-Requested-With");
                    e.HttpClient.Request.Headers.RemoveHeader("Expires");
                    // Мягкие заголовки кеширования
                    e.HttpClient.Request.Headers.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                    e.HttpClient.Request.Headers.AddHeader("Pragma", "no-cache");
                    e.HttpClient.Request.Headers.AddHeader("Expires", "0");
                    e.HttpClient.Request.Headers.AddHeader("X-Requested-With", "XMLHttpRequest");
                    e.HttpClient.Request.Headers.AddHeader("Accept", "*/*");

                }

                string? thisBody = null;

                var record = new TrafficRecord
                {
                    RequestUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url),
                    RequestMethod = e.HttpClient.Request.Method,
                    RequestTime = DateTime.UtcNow,
                    RequestHeaders = e.HttpClient.Request.Headers.Select(h => new { h.Name, h.Value })
                        .ToDictionary(h => h.Name, h => h.Value)
                };

                try
                {
                    // Получаем тело ответа
                    record.RequestBodyRaw = await e.GetRequestBodyAsString();
                    record.RequestBodyBase64 = Convert.ToBase64String(e.HttpClient.Request.Body);
                    thisBody = record.RequestBodyRaw;
                }
                catch (Exception ex)
                {
                    //Log.Warning($"[{this.ProxyEndpoint}] Не удалось получить тело запроса: {ex.Message}");
                }

                // Сохраняем метаданные запроса
                e.UserData = record;

                var domainsForReplace = this.DomainsForReplaces.Where(x => Regex.IsMatch(thisUrl, x.DomainRegex)).ToArray();
                if (domainsForReplace.Any())
                {
                    foreach (var replaceData in domainsForReplace)
                    {
                        thisUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url);

                        // Новый URL после замены
                        string newUrl = Regex.Replace(thisUrl, replaceData.RegexFromReplace, replaceData.TextToReplace);

                        

                        // Если требуется изменить заголовок Host для нового домена
                        var originalUri = new Uri(thisUrl);
                        var newUri = new Uri(newUrl);
                        if (originalUri.Host != newUri.Host)
                        {
                            e.HttpClient.Request.Headers.RemoveHeader("Host");
                            e.HttpClient.Request.Headers.AddHeader("Host", newUri.Host);//
                        }

                        // Устанавливаем новый URL для запроса
                        e.HttpClient.Request.Url = newUri.AbsoluteUri;
                        record.RequestUrl = newUrl;

                    }

                    // Сохраняем метаданные запроса
                    e.UserData = record;
                    var nowUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url);
                    Log.Information($"[{this.ProxyEndpoint}] Redirecting from {startUrl} to: {nowUrl}");
                }
                // Проверяем наличие текста в запросе
                if (!string.IsNullOrEmpty(thisBody))
                {
                    var changeRequestsArray = this.ChangeRequests.Where(x => Regex.IsMatch(thisUrl, x.DomainRegex)).ToArray();
                    foreach (var changeRequest in changeRequestsArray)
                    {
                        for (int i = 0; i < changeRequest.RegexesFromReplace.Count; i++)
                        {
                            var regexFrom = changeRequest.RegexesFromReplace[i];
                            var textTo = changeRequest.TextToReplace[i];

                            thisBody = await e.GetRequestBodyAsString();
                            if (!Regex.IsMatch(thisBody, regexFrom)) continue;
                            var newBody = Regex.Replace(thisBody, regexFrom, textTo);
                            e.SetRequestBodyString(newBody);
                            record.RequestBodyRaw = await e.GetRequestBodyAsString();
                            record.RequestBodyBase64 = Convert.ToBase64String(e.HttpClient.Request.Body);
                            isModified = true;
                            Log.Information($"[{this.ProxyEndpoint}] Changed REQUEST body for {startUrl} to: {record.RequestBodyRaw}");
                        }
                    }
                }
                

                if (isModified)
                    return; // Выход после обработки в случае модификаций

            }
            catch (Exception exception)
            {
                Log.Information($"[OnBeforeRequest] Error: {exception.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task OnBeforeResponse(object sender, SessionEventArgs e)
        {
            var startUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url);
            var thisUrl = HttpUtility.UrlDecode(e.HttpClient.Request.Url);
            string? thisBody = null;
            bool isModified = false;
            // Получаем сохраненную ранее запись
            var record = e.UserData as TrafficRecord;
            if (record == null) return;

            record.ResponseTime = DateTime.UtcNow;
            record.ResponseDuration = record.ResponseTime - record.RequestTime;
            record.ResponseStatusCode = e.HttpClient.Response.StatusCode;
            record.ResponseStatusDescription = e.HttpClient.Response.StatusDescription;
            record.ResponseHeaders = e.HttpClient.Response.Headers
                .Select(h => new { h.Name, h.Value })
                .ToDictionary(h => h.Name, h => h.Value);
            record.ContentType = e.HttpClient.Response.ContentType ?? string.Empty;
            record.ContentLength = e.HttpClient.Response.ContentLength;

            // Получаем тело ответа
            try
            {
                // Получаем тело ответа
                record.ResponseBodyRaw = await e.GetResponseBodyAsString();
                record.ResponseBodyBase64 = Convert.ToBase64String(e.HttpClient.Response.Body);
                thisBody = record.ResponseBodyRaw;
            }
            catch (Exception ex)
            {
                //Log.Warning($"[{this.ProxyEndpoint}] Не удалось получить тело ответа: {ex.Message}");
            }


            // Проверяем наличие текста в запросе
            if (!string.IsNullOrEmpty(thisBody))
            {
                var changeResponseArray = this.ChangeResponses.Where(x => Regex.IsMatch(thisUrl, x.DomainRegex)).ToArray();
                foreach (var changeRequest in changeResponseArray)
                {
                    for (int i = 0; i < changeRequest.RegexesFromReplace.Count; i++)
                    {
                        var regexFrom = changeRequest.RegexesFromReplace[i];
                        var textTo = changeRequest.TextToReplace[i];
                        thisBody = await e.GetResponseBodyAsString();
                        if (!Regex.IsMatch(thisBody, regexFrom)) continue;
                        var newBody = Regex.Replace(thisBody, regexFrom, textTo);
                        e.SetResponseBodyString(newBody);
                        record.ResponseBodyRaw = await e.GetResponseBodyAsString();
                        record.ResponseBodyBase64 = Convert.ToBase64String(e.HttpClient.Response.Body);
                        isModified = true;
                        Log.Information($"[{this.ProxyEndpoint}] Changed RESPONSE body for {startUrl} to: {record.ResponseBodyRaw}");

                    }
                }
            }

            // Добавляем запись в монитор трафика
            _trafficMonitor.AddTrafficRecord(record);

            if (isModified)
                return; // Выход после обработки в случае модификаций

            

            await Task.CompletedTask;
        }

        // Метод для получения записей трафика
        public IEnumerable<TrafficRecord> GetTrafficRecords()
        {
            return this._trafficMonitor.GetTrafficRecords();
        }

        // Метод для очистки записей трафика
        public void ClearTrafficRecords()
        {
            this._trafficMonitor.ClearTrafficRecords();
        }

        public List<DomainsForReplace> AddDomainForReplace(DomainsForReplace addListData)
        {
            this.DomainsForReplaces.Add(addListData);
            return this.DomainsForReplaces;
        }
        public List<ChangeBody> AddChangeRequest(ChangeBody addListData)
        {
            this.ChangeRequests.Add(addListData);
            return this.ChangeRequests;
        }
        public List<ChangeBody> AddChangeResponse(ChangeBody addListData)
        {
            this.ChangeResponses.Add(addListData);
            return this.ChangeResponses;
        }
    }

  
    public class DomainsForReplace
    {
        public string DomainRegex { get; set; } = string.Empty;
        public string RegexFromReplace { get; set; } = string.Empty;
        public string TextToReplace { get; set; } = string.Empty;
    }
    public class ChangeBody
    {
        public string DomainRegex { get; set; } = string.Empty;
        public List<string> RegexesFromReplace { get; set; } = [];
        public List<string> TextToReplace { get; set; } = [];
    }
    public class TrafficRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string RequestUrl { get; set; }
        public string RequestMethod { get; set; }
        public DateTime RequestTime { get; set; }
        public DateTime ResponseTime { get; set; }
        public TimeSpan ResponseDuration { get; set; }
        public int ResponseStatusCode { get; set; }
        public string ResponseStatusDescription { get; set; }

        // Заголовки
        public Dictionary<string, string> RequestHeaders { get; set; }
        public Dictionary<string, string> ResponseHeaders { get; set; }

        // Тела запроса и ответа
        public string RequestBodyRaw { get; set; }
        public string RequestBodyBase64 { get; set; }
        public string ResponseBodyRaw { get; set; }
        public string ResponseBodyBase64 { get; set; }

        // Дополнительная информация
        public string ContentType { get; set; }
        public long ContentLength { get; set; }
        public string ClientIpAddress { get; set; }
    }
    public class TrafficMonitor
    {
        private readonly ConcurrentBag<TrafficRecord> _trafficRecords = new();
        private readonly object _lockObject = new();

        public void AddTrafficRecord(TrafficRecord record)
        {
            _trafficRecords.Add(record);

            // Опционально: ограничение размера коллекции
            if (_trafficRecords.Count > 1000)
            {
                // Удаление старых записей
                _trafficRecords.TakeWhile(r => r.RequestTime < DateTime.UtcNow.AddHours(-1)).ToList()
                    .ForEach(r => _trafficRecords.TryTake(out _));
            }
        }

        public IEnumerable<TrafficRecord> GetTrafficRecords()
        {
            return _trafficRecords.ToList();
        }

        public void ClearTrafficRecords()
        {
            _trafficRecords.Clear();
        }
    }
}

