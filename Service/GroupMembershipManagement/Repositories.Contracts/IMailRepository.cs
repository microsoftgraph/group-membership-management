using Microsoft.Graph;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailRepository
    {
        Task SendMail(string subject, string content, string toEmailAddress);
    }
}