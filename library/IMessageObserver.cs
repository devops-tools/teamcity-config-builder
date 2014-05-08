namespace TeamCityConfigBuilder.Library
{
    public interface IMessageObserver
    {
        void Notify(string message);
        void Notify(string format, params object[] args);
    }
}