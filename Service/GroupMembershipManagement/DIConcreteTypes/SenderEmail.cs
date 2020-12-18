using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class SenderEmail<T> : ISenderEmail<T>
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public SenderEmail(string email, string password)
        {
            Email = email;
            Password = password;
        }

        public SenderEmail()
        {

        }
    }
}
