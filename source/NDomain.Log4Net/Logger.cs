using System;
using NDomain.Logging;

namespace NDomain.Log4Net
{
    public class Logger : ILogger
    {
        private readonly log4net.ILog _logger;

        public Logger(log4net.ILog logger)
        {
            _logger = logger;
        }

        public void Debug(string message, params object[] args)
        {
            _logger.DebugFormat(message, args);
        }

        public void Info(string message, params object[] args)
        {
            _logger.InfoFormat(message, args);
        }

        public void Warn(string message, params object[] args)
        {
            _logger.WarnFormat(message, args);
        }

        public void Warn(Exception exception, string message, params object[] args)
        {
            string msg = string.Format(message, args);
            _logger.Warn(msg, exception);
        }

        public void Error(string message, params object[] args)
        {
            _logger.ErrorFormat(message, args);
        }

        public void Error(Exception exception, string message, params object[] args)
        {
            string msg = string.Format(message, args);            
            _logger.Error(msg, exception);
        }

        public void Fatal(string message, params object[] args)
        {
            _logger.FatalFormat(message, args);
        }

        public void Fatal(Exception exception, string message, params object[] args)
        {
            string msg = string.Format(message, args);
            _logger.Fatal(msg, exception);
        }
    }
}
