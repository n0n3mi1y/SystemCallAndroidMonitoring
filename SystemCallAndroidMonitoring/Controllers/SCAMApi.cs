using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using SystemCallAndroidMonitoring.Titanium;
using static SystemCallAndroidMonitoring.Controllers.TitaniumInstanceApiResponse;

namespace SystemCallAndroidMonitoring.Controllers
{
    [ApiController]
    [Route("scamapi")]
    public class ScamApi : Controller
    {
        [HttpGet("newInstance")]
        public object NewInstance(string proxy_row = null, int proxyKillDelay = 300)
        {
            try
            {
                if (!string.IsNullOrEmpty(proxy_row))
                {

                    var (address, port, username, password, protocol) = ParseProxyString(proxy_row);
                    var newInstance = new TitaniumInstance(address,
                        port,
                        username,
                        password,
                        protocol, proxyKillDelay);
                    return new NewInstanceOk()
                    {
                        IsSuccess = true,
                        InstanceData = newInstance
                    };
                }
                else
                {
                    var newInstance = new TitaniumInstance(maximumActivityDelaySeconds: proxyKillDelay);
                    return new NewInstanceOk()
                    {
                        IsSuccess = true,
                        InstanceData = newInstance
                    };
                }
            }
            catch (Exception e)
            {
                return new NewInstanceBad()
                {
                    IsSuccess = false,
                    ErrorText = e.Message
                };
            }
            
        }

        [HttpGet("getInstances")]
        public List<GetInstances> GetInstances()
        {   
            var allInstances = AllInstances.ActiveInstances;
            var returnList = new List<GetInstances>();
            foreach (var instance in allInstances)
                returnList.Add(new(){EndPoint = instance.Key, Instance = instance.Value});
            return returnList;
        }

        [HttpGet("getTraffic")]
        public GetTrafficResponse GetTraffic(string endPoint)
        {
            if (AllInstances.ActiveInstances.TryGetValue(endPoint, out var titaniumInstance))
                return new GetTrafficResponse()
                {
                    IsSuccess = true,
                    TrafficRecords = titaniumInstance.GetTrafficRecords().ToList()
                };
            else
            {
                return new GetTrafficResponse()
                {
                    IsSuccess = false
                };
            }
        }
        [HttpGet("clearTraffic")]
        public GetTrafficResponse ClearTraffic(string endPoint)
        {
            if (AllInstances.ActiveInstances.TryGetValue(endPoint, out var titaniumInstance))
            {
                titaniumInstance.ClearTrafficRecords();
                return new GetTrafficResponse()
                {
                    IsSuccess = true,
                    TrafficRecords = titaniumInstance.GetTrafficRecords().ToList()
                };
            }
                
            else
            {
                return new GetTrafficResponse()
                {
                    IsSuccess = false
                };
            }
        }

        [HttpPost("addDomainForChange")]
        public AddDomainForChangeResponse AddDomainForChange(string endPoint, DomainsForReplace replaceData)
        {
            if (AllInstances.ActiveInstances.TryGetValue(endPoint, out var titaniumInstance))
            {
                return new AddDomainForChangeResponse()
                {
                    IsSuccess = true,
                    DomainsForReplaceData = titaniumInstance.AddDomainForReplace(replaceData)
                };
            }
            else
            {
                return new AddDomainForChangeResponse()
                {
                    IsSuccess = false,
                    ErrorText = $"Не удалось найти инстанс по эндпоинту {endPoint}"
                };
            }
        }

        [HttpPost("addChangeRequest")]
        public AddChangeBodyResponse AddChangeRequest(string endPoint, ChangeBody replaceData)
        {
            if (AllInstances.ActiveInstances.TryGetValue(endPoint, out var titaniumInstance))
            {
                if (replaceData.RegexesFromReplace.Count == replaceData.TextToReplace.Count)
                    return new AddChangeBodyResponse()
                    {
                        IsSuccess = true,
                        ChangeBodyArray = titaniumInstance.AddChangeRequest(replaceData)
                    };
                else return new AddChangeBodyResponse()
                {
                    IsSuccess = false,
                    ErrorText = $"Несоответствие количества Regex для замены и конечных значений"
                };
            }
            else
            {
                return new AddChangeBodyResponse()
                {
                    IsSuccess = false,
                    ErrorText = $"Не удалось найти инстанс по эндпоинту {endPoint}"
                };
            }
        }

        [HttpPost("addChangeResponse")]
        public AddChangeBodyResponse AddChangeResponse(string endPoint, ChangeBody replaceData)
        {
            if (AllInstances.ActiveInstances.TryGetValue(endPoint, out var titaniumInstance))
            {
                if (replaceData.RegexesFromReplace.Count == replaceData.TextToReplace.Count)
                    return new AddChangeBodyResponse()
                    {
                        IsSuccess = true,
                        ChangeBodyArray = titaniumInstance.AddChangeResponse(replaceData)
                    };
                else return new AddChangeBodyResponse()
                {
                    IsSuccess = false,
                    ErrorText = $"Несоответствие количества Regex для замены и конечных значений"
                };
            }
            else
            {
                return new AddChangeBodyResponse()
                {
                    IsSuccess = false,
                    ErrorText = $"Не удалось найти инстанс по эндпоинту {endPoint}"
                };
            }
        }

        private static (string? Address, int Port, string? Username, string? Password, string Protocol) ParseProxyString(string proxyString)
        {
            if (string.IsNullOrWhiteSpace(proxyString))
            {
                return (null, 0, null, null, "HTTP");
            }

            string protocol = "HTTP";
            string address;
            int port = 0;
            string? username = null;
            string? password = null;

            // Проверка наличия протокола
            var protocolMatch = Regex.Match(proxyString, @"^(http|socks4|socks5)://", RegexOptions.IgnoreCase);
            if (protocolMatch.Success)
            {
                protocol = protocolMatch.Groups[1].Value.ToUpper();
                proxyString = proxyString.Substring(protocolMatch.Length);
            }

            // Извлечение логина и пароля
            var credentialsMatch = Regex.Match(proxyString, @"^(([^:@]+):([^@]+)@)?(.+)$");
            if (credentialsMatch.Success)
            {
                // Если есть логин и пароль
                if (!string.IsNullOrEmpty(credentialsMatch.Groups[2].Value))
                {
                    username = credentialsMatch.Groups[2].Value;
                    password = credentialsMatch.Groups[3].Value;
                }

                address = credentialsMatch.Groups[4].Value;
            }
            else
            {
                address = proxyString;
            }

            // Разделение адреса и порта
            var addressPortMatch = Regex.Match(address, @"^(.+):(\d+)$");
            if (addressPortMatch.Success)
            {
                address = addressPortMatch.Groups[1].Value;
                if (!int.TryParse(addressPortMatch.Groups[2].Value, out port))
                {
                    throw new ArgumentException("Некорректный порт");
                }
            }

            return (address, port, username, password, protocol);
        }
    }


    public class TitaniumInstanceApiResponse
    {
        public class NewInstanceBase
        {
            public bool IsSuccess { get; set; } = false;

        }

        public class NewInstanceOk : NewInstanceBase
        {
            public TitaniumInstance InstanceData { get; set; } = null;
        }

        public class NewInstanceBad : NewInstanceBase
        {
            public string ErrorText { get; set; } = string.Empty;
        }

        public class GetInstances
        {
            public string EndPoint { get; set; } = string.Empty;
            public TitaniumInstance Instance { get; set; } = null;
        }

        public class GetTrafficResponse
        {
            public bool IsSuccess { get; set; } = false;
            public List<TrafficRecord> TrafficRecords { get; set; }
        }
        public class AddDomainForChangeResponse
        {
            public bool IsSuccess { get; set; } = false;
            public string ErrorText { get; set; } = string.Empty;
            public List<DomainsForReplace> DomainsForReplaceData { get; set; }
        }
        public class AddChangeBodyResponse
        {
            public bool IsSuccess { get; set; } = false;
            public string ErrorText { get; set; } = string.Empty;
            public List<ChangeBody> ChangeBodyArray { get; set; } = new();
        }
    }
}
