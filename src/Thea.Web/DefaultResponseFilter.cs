using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Thea.Web;

class DefaultResponseFilter : IResponseFilter
{
    public Task<string> ProcessRequest(HttpContext context, Stream readableStream, Exception exception)
        => this.ReadBody(readableStream);
    private async Task<string> ReadBody(Stream stream)
    {
        stream.Position = 0;
        var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();
        stream.Position = 0;
        return result;
    }
}