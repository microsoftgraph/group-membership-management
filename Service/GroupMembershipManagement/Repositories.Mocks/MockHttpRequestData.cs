using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Moq;
using System.Security.Claims;

namespace Repositories.Mocks
{
    public class MockHttpRequestData : HttpRequestData
    {
        public MockHttpRequestData(FunctionContext functionContext, string content) : base(functionContext)
        {
            Headers = new HttpHeadersCollection();
            Body = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Method = "POST";
            Url = new Uri("http://localhost");
        }

        public override Stream Body { get; }

        public override HttpHeadersCollection Headers { get; }

        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = new List<IHttpCookie>();

        public override Uri Url { get; }

        public override string Method { get; }

        public override IEnumerable<ClaimsIdentity> Identities => throw new NotImplementedException();

        public override HttpResponseData CreateResponse()
        {
            var response = new MockHttpResponseData(FunctionContext)
            {
                StatusCode = HttpStatusCode.OK, 
                Body = new MemoryStream()
            };
            return response;
        }
    }
    public class MockHttpResponseData : HttpResponseData
    {
        public MockHttpResponseData(FunctionContext functionContext) : base(functionContext)
        {
            Headers = new HttpHeadersCollection();
            Body = new MemoryStream();
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; }
        public override Stream Body { get; set; }
        public override HttpCookies Cookies => new MockHttpCookies(); 
    }
    public class MockHttpCookies : HttpCookies
    {
        private readonly Dictionary<string, HttpCookie> _cookies = new Dictionary<string, HttpCookie>();

        public override void Append(string name, string value)
        {
            _cookies[name] = new HttpCookie(name, value);
        }

        public override void Append(IHttpCookie cookie)
        {
            throw new NotImplementedException();
        }

        public override IHttpCookie CreateNew()
        {
            throw new NotImplementedException();
        }
    }
}


