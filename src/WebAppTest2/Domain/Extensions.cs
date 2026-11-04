using MySqlConnector;
using System.Data;
using System.Text;

namespace WebAppTest2.Domain;

public static class Extensions
{
    public static string ToMySqlParametersString(this IDataParameterCollection dbParameters)
    {
        if (dbParameters == null || dbParameters.Count == 0)
            return "None";
        var builder = new StringBuilder();
        foreach (var parameter in dbParameters)
        {
            var dbParameter = parameter as MySqlParameter;
            builder.Append($"{dbParameter.ParameterName}:{{MySqlDbType={dbParameter.MySqlDbType}, Value={dbParameter.Value}}};  ");
        }
        return builder.ToString();
    }
}