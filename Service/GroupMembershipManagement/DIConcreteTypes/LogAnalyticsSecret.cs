using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class LogAnalyticsSecret<T> : ILogAnalyticsSecret<T>
    {
        public string WorkSpaceId { get; set; }
        public string SharedKey { get; set; }
        public string Location { get; set; }
        public LogAnalyticsSecret(string workSpaceId, string sharedKey, string location)
        {
            WorkSpaceId = workSpaceId;
            SharedKey = sharedKey;
            Location = location;
        }
		public LogAnalyticsSecret()
		{

		}
    }
}
