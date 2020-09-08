using Entities;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
	public class SGMembershipCalculatorFunction
    {
		private readonly SGMembershipCalculator _calculator;

		public SGMembershipCalculatorFunction(SGMembershipCalculator calculator)
		{
            _calculator = calculator;
		}

		[FunctionName(nameof(SecurityGroup))]
        public async Task Run([ServiceBusTrigger("%serviceBusSyncJobTopic%", "SecurityGroup", Connection = "serviceBusTopicConnection")]Message message)
        {
            await _calculator.SendMembership(JsonConvert.DeserializeObject<SyncJob>(Encoding.UTF8.GetString(message.Body)));
		}
    }
}
