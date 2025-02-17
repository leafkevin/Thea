using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Thea.Web;

public interface IResponseFilter
{
    Task<string> ProcessRequest(HttpContext context, Stream readableStream, Exception exception);
}