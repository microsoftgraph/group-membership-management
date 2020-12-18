using Repositories.Contracts;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockMailRepository : IMailRepository
    {
        public Task SendMail(string subject, string content, string toEmailAddress)
        {
            return Task.CompletedTask;
        }        
    }
}
