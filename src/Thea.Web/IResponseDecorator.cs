using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Thea.Web;

public interface IResponseDecorator
{
    Task<(LogLevel, string)> ProcessRequest(HttpContext context, Stream readableStream, LogLevel logLevel, Exception exception);
}