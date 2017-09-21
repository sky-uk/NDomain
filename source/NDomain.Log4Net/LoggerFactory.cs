using System;
using NDomain.Logging;

namespace NDomain.Log4Net
{
    public class LoggerFactory : ILoggerFactory
    {
        public ILogger GetLogger(string name)
        {
            return new Logger(log4net.LogManager.GetLogger(name));
        }

        public ILogger GetLogger(Type type)
        {
            return new Logger(log4net.LogManager.GetLogger(type));
        }
    }
}