using NDomain.Configuration;

namespace NDomain.Log4Net
{
    public static class Log4NetConfigurator
    {
        public static LoggingConfigurator WithLog4Net(this LoggingConfigurator configurator)
        {
            configurator.LoggerFactory = new LoggerFactory();

            return configurator;
        }
    }
}