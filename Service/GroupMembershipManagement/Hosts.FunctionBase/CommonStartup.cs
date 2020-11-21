using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Runtime.CompilerServices;

namespace Hosts.FunctionBase
{
    public class CommonStartup : FunctionsStartup
    {
        public CommonStartup() { }
        protected IConfigurationRoot _configuration;
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var serviceProvider = builder.Services.BuildServiceProvider();
            var configurationRoot = serviceProvider.GetService<IConfiguration>();
            var configurationBuilder = new ConfigurationBuilder().AddEnvironmentVariables();

            configurationBuilder.AddConfiguration(configurationRoot);

            _configuration = configurationBuilder.Build();
            builder.Services.Replace(ServiceDescriptor.Singleton(typeof(IConfiguration), _configuration));
        }

		public string GetValueOrThrow(string key, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
		{
			var value = _configuration.GetValue<string>(key);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentNullException($"Could not start because of missing configuration option: {key}. Requested by file {callerFile}:{callerLine}.");
            return value;
        }
    }
}
