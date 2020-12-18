namespace Repositories.Contracts.InjectConfig
{
    public interface ISenderEmail<TType>
    {
        string Email { get; }
        string Password { get; }
    }
}
