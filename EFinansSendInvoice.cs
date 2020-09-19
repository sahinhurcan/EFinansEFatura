    class SessionInfo {
        const double EFINANS_DEFAULT_COOKIE_EXPIRATION = 30;
        public string Key, Cookie;
        public DateTime ExpirationTime;

        public SessionInfo(string key, string cookie, DateTime? expirationTime = null) {
            Key = key;
            Cookie = cookie;
            ExpirationTime = expirationTime ?? DateTime.Now.AddMinutes(EFINANS_DEFAULT_COOKIE_EXPIRATION);
        }
    }

    public class EFinansSendEInvoice : ICommandSendEInvoice {

        #region Props & Fields
        private Task<ServiceResponse> _result;

        private ConnectorServiceClient _connectorWsClient;
        private UserServiceClient _userWsClient;

        public InvoiceType Invoice { get; set; }
        public IMapper IMapper { get; set; }
        public ServiceInfo ServiceInfo { get; set; }

        static Dictionary<string, SessionInfo> _sessionsDictionary;
        Dictionary<string, SessionInfo> _sessions {
            get {
                return _sessionsDictionary ?? (_sessionsDictionary = new Dictionary<string, SessionInfo>());
            }
        }
        #endregion

        #region Ctors
        public EFinansSendEInvoice(
            InvoiceType invoiceType = null,
            IMapper mapper = null) {

            this.IMapper = mapper;
            this.Invoice = invoiceType;
        } 
        #endregion

        public Task<ServiceResponse> TaskResult()
        {
            return this._result;
        }

        

        /// <summary>
        /// UserServiceClient ve ConnectorServiceClient sınıflarından nesneler oluşturulur.
        /// Önce wsLogin ile kullanıcı girişi yapılır ve dönen cookie bilgisi connectorService nesnesine her talebi için atanır.
        /// </summary>
        /// <returns>ServiceResponse Türünde yanıt döner</returns>
        public async Task Execute() {

            var result = new ServiceResponse() {
                Hatali = false,
            };

            if (Invoice == null) { throw new ArgumentNullException("Invoice boş olamaz!"); }

            try {
                this._connectorWsClient = CreateConnectorServiceClient(ServiceInfo.ServiceUrl, ServiceInfo.UserName, ServiceInfo.Password);
                this._userWsClient = CreateUserServiceClient(ServiceInfo.UserServiceUrl, ServiceInfo.UserName, ServiceInfo.Password);

                var cookie = GetValidCookie(_userWsClient);
                string belgeOid;

                using (OperationContextScope ocs = new OperationContextScope(_connectorWsClient.InnerChannel)) {
                    var requestProp = new HttpRequestMessageProperty();
                    requestProp.Headers["Set-Cookie"] = cookie;
                    OperationContext.Current.OutgoingMessageProperties[HttpRequestMessageProperty.Name] = requestProp;
                    
                    var request = PrepareGidenBelgeParametreleri();
                    belgeOid = _connectorWsClient.belgeGonderExt(request);
                }

                result.Sonuc = string.IsNullOrEmpty(belgeOid)
                    ? "İşlem tamamlandı!"
                    : "İşlem başarıyla tamamlandı";
                result.Data = new { belgeOid };

            } catch (Exception ex) {
                result.Hatali = true;
                result.Istisna += ex.ToString();
            }

            this._result = Task.FromResult(result);
        }


        BasicHttpBinding CreateBasicHttpBinding() {
            BasicHttpBinding basicHttpBinding = null;
            basicHttpBinding = new BasicHttpBinding(securityMode: BasicHttpSecurityMode.Transport);
            basicHttpBinding.AllowCookies = true;
            basicHttpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.None;
            basicHttpBinding.MaxBufferSize = int.MaxValue;
            basicHttpBinding.MaxReceivedMessageSize = long.MaxValue;
            basicHttpBinding.ReaderQuotas.MaxStringContentLength = int.MaxValue;
            return basicHttpBinding;
        }

        ConnectorServiceClient CreateConnectorServiceClient(string url, string username, string password) {
            var basicHttpBinding = CreateBasicHttpBinding();
            var endpointAddress = new EndpointAddress(url);
            var client = new ConnectorServiceClient(basicHttpBinding, endpointAddress);
            client.ClientCredentials.UserName.UserName = username;
            client.ClientCredentials.UserName.Password = password;

            return client;
        }

        UserServiceClient CreateUserServiceClient(string url, string username, string password) {
            var basicHttpBinding = CreateBasicHttpBinding();
            var endpointAddress = new EndpointAddress(url);

            var client = new UserServiceClient(basicHttpBinding, endpointAddress);
            client.ClientCredentials.UserName.UserName = username;
            client.ClientCredentials.UserName.Password = password;
            return client;
        }

        private string GetValidCookie(UserServiceClient userWsClient) {
            var cookie = string.Empty;
            var cookieKey = $"{ServiceInfo.UserName}_{ServiceInfo.Password}";

            bool isCookieExistAndValid = _sessions.ContainsKey(cookieKey) && _sessions[cookieKey].ExpirationTime > DateTime.Now;
            cookie = isCookieExistAndValid ? _sessions[cookieKey].Cookie : GetCookieFromEFinans(userWsClient);
            return cookie;
        }

        private string GetCookieFromEFinans(UserServiceClient userWsClient) {
            string cookie;
            using (var scopeUser = new OperationContextScope(userWsClient.InnerChannel)) {
                userWsClient.wsLogin(ServiceInfo.UserName, ServiceInfo.Password, "tr");
                var httpProperties = (HttpResponseMessageProperty)OperationContext.Current.IncomingMessageProperties[HttpResponseMessageProperty.Name];
                var headers = httpProperties.Headers;
                cookie = httpProperties.Headers["Set-Cookie"];
            }

            return cookie;
        }

        private gidenBelgeParametreleri PrepareGidenBelgeParametreleri() {
            byte[] barrInvoice = Serialization.SerializeToBytes(this.Invoice);

            var request = new gidenBelgeParametreleri() {
                vergiTcKimlikNo = Invoice.AccountingSupplierParty.Party.PartyIdentification[0].ID.Value,
                belgeTuru = "FATURA_UBL",
                belgeNo = Invoice.ID.Value,
                veri = barrInvoice,
                belgeHash = Hash.HashArray(barrInvoice),
                mimeType = "application/xml",
                belgeVersiyon = "1.2",
                erpKodu = this.ServiceInfo.CorporateCode,
            };
            return request;
        }

        public void Dispose() {
            _result?.Dispose();
            (this._connectorWsClient as ICommunicationObject)?.Close();
            (this._userWsClient as ICommunicationObject)?.Close();
        }
    }
